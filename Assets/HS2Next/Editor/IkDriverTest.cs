// IK driver tests (Phase 3.4 package B; measurements U1 (ops), U3, U7).
// Run: -executeMethod IkDriverTest.Run [-only D1+D5] (',' or '+'; the runner splits ',' arguments) (headless, -nographics, edit mode, ManualStepping).
// Cards: the SFW visual targets of Tools/targets.json (BR-Chan female, HERO107 male). Characters are added through
// SceneOps.AddCharaFromCard into a session + runtime; the IK and FK drivers are the defaults (attached by the test when
// they are not). D5 / D7 use the SFW-named comparison poses (P1–P8) from <HS2>/UserData/studio/pose
// (read-only); D8 the 15 FK compare poses of Tools/compare_poses.json. D13 is the FkWriterTest row (run it separately).
// Report: Spike/out/pose/ik_driver_report.txt, first line "SUMMARY: N PASS, M FAIL"; EditorApplication.Exit(1) on any FAIL.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Content;
using HS2Next.Motion;
using HS2Next.Motion.Fk;
using HS2Next.Motion.Ik;
using HS2Next.Motion.Look;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class IkDriverTest
{
    const float Dt = 1f / 60f;
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "pose");
    static StringBuilder rep;
    static int passes, fails;
    static string current = "";
    static readonly Dictionary<string, (int pass, int fail)> perTest = new Dictionary<string, (int, int)>();
    static string[] only;
    static RuntimeServices services;
    static string femaleCard, maleCard, poseRoot;
    static readonly AnimeRef Idle = new AnimeRef(ContentIds.Vanilla, 19, 0, 0);
    static readonly List<string> measurements = new List<string>();

    /// <summary>The comparison poses (paths relative to UserData/studio/pose).</summary>
    static readonly string[] ComparePoses =
    {
        "CHAIRS/Standing next to.dat", "STANDING/Floor/Peace.dat", "STANDING/Floor/Im cute.dat", "STANDING/Floor/Hands up 2.dat",
        "TINNY HOME 6/Lying on floor.dat", "CHAIRS/Sitting 4.dat", "SOFA/Sitting edge.dat", "TINNY HOME 6/Near TV.dat",
    };

    static string Arg(string name)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    static void Section(string id, string title) { current = id; rep.AppendLine(); rep.AppendLine($"== {id} {title}"); }
    static void Info(string s) => rep.AppendLine("   " + s);
    static void Measure(string s) { measurements.Add($"[{current}] {s}"); Info("MEASURE " + s); }
    static void Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  [{current}] {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
        perTest.TryGetValue(current, out var v);
        perTest[current] = ok ? (v.pass + 1, v.fail) : (v.pass, v.fail + 1);
    }

    static void Guard(string id, Action a)
    {
        if (only != null && !only.Contains(id)) return;
        current = id;
        bool ex = IkOptions.Exclusive, ff = IkOptions.MissingTargetsFirstFrame, rl = IkOptions.RotationLimits;
        try { a(); }
        catch (Exception e) { Check("exception", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        finally
        {
            IkOptions.Exclusive = ex; IkOptions.MissingTargetsFirstFrame = ff; IkOptions.RotationLimits = rl;
            LookTargets.DefaultCamera = null;
        }
    }

    public static void Run()
    {
        rep = new StringBuilder(); passes = fails = 0; perTest.Clear(); measurements.Clear();
        var o = Arg("-only");
        only = o == null ? null : o.Split(',', '+').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        Directory.CreateDirectory(OutDir);
        var sw = Stopwatch.StartNew();
        var oldIk = CharaPoseDrivers.Ik; var oldFk = CharaPoseDrivers.Fk;
        var oldSuspend = StudioDefaultPoseScope.SuspendPoseDrivers;
        var head = new StringBuilder();
        try
        {
            if (!(CharaPoseDrivers.Ik is IkDriver)) CharaPoseDrivers.Ik = IkDriver.Instance;
            if (!(CharaPoseDrivers.Fk is FkDriver)) CharaPoseDrivers.Fk = FkDriver.Instance;
            if (StudioDefaultPoseScope.SuspendPoseDrivers == null) StudioDefaultPoseScope.SuspendPoseDrivers = CharaPoseDrivers.SuspendAll;
            services = SceneRuntimeTest.NewServices(head);
            var t = TestTargets.Load();
            femaleCard = TestTargets.Abs(t.visual.female);
            maleCard = TestTargets.Abs(t.visual.male);
            poseRoot = Path.Combine(services.Hs2Root, "UserData", "studio", "pose");
            rep.AppendLine($"IK driver test (Unity {Application.unityVersion}); IK driver {(oldIk is IkDriver ? "registered by default" : "attached by the test")}");
            rep.Append(head);
            Guard("D1", D1);
            Guard("D2", D2);
            Guard("D3", D3);
            Guard("D4", D4);
            Guard("D5", D5);
            Guard("D6", D6);
            Guard("D7", D7);
            Guard("D8", D8);
            Guard("D9", D9);
            Guard("D10", D10);
            Guard("D11", D11);
            Guard("D12", D12);
            Guard("D14", D14);
        }
        catch (Exception e) { current = "run"; Check("run (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        finally
        {
            CharaPoseDrivers.Ik = oldIk; CharaPoseDrivers.Fk = oldFk;
            StudioDefaultPoseScope.SuspendPoseDrivers = oldSuspend;
            LookTargets.DefaultCamera = null;
            if (camGo) Object.DestroyImmediate(camGo);
        }
        var sum = new StringBuilder();
        sum.AppendLine($"SUMMARY: {passes} PASS, {fails} FAIL ({sw.Elapsed.TotalSeconds:F0} s)");
        foreach (var kv in perTest.OrderBy(k => k.Key, StringComparer.Ordinal)) sum.AppendLine($"  {kv.Key}: {kv.Value.pass} pass, {kv.Value.fail} fail");
        if (measurements.Count > 0) { sum.AppendLine("measurements:"); foreach (var m in measurements) sum.AppendLine("  " + m); }
        rep.Insert(0, sum.ToString());
        File.WriteAllText(Path.Combine(OutDir, "ik_driver_report.txt"), rep.ToString());
        services?.Catalog?.UnloadAll();
        Debug.Log($"IK DRIVER TEST DONE: {passes} PASS, {fails} FAIL");
        if (fails > 0) TestExit.Exit(1);
    }

    // ================================================================ fixture

    sealed class Env : IDisposable
    {
        public readonly SceneSession S;
        public readonly SceneRuntime Rt;
        public readonly IkOps Ik;
        public readonly FkOps Fk;
        public readonly KinematicCopies Copies;

        public Env(ulong seed = 0x1c1c1c1cUL)
        {
            S = SceneRuntimeTest.NewSession(services, seed);
            Rt = new SceneRuntime(S, services, null, false);
            Ik = new IkOps(Rt);
            Fk = new FkOps(Rt);
            Copies = new KinematicCopies(Rt);
        }

        public string Add(string card)
        {
            var r = Rt.Ops.AddCharaFromCard(card);
            if (r == null || !r.Ok) throw new Exception("add " + Path.GetFileName(card) + ": " + r);
            Rt.ApplyNow();
            return r.NewId;
        }

        /// <summary>A character with Idle at speed 0 and its physics stages off, stepped twice.</summary>
        public string AddStill(string card, AnimeRef anime = null, float speed = 0f)
        {
            var id = Add(card);
            NoPhysics(C(id));
            S.SetAnimation(id, anime ?? Idle);
            Set(id, "chara.anime.speed", speed);
            Set(id, "chara.look.neck", 4);   // FIX (the comparison protocol; the neck look has no history then)
            Rt.ApplyNow();
            Step(2);
            return id;
        }

        public CharaRealization C(string id) => Rt.FindChara(id);
        public IkCharacter IkC(string id) => IkDriver.Of(C(id));
        public FkCharacter FkC(string id) => FkDriver.Of(C(id));
        public CharaNodeState St(string id) => (CharaNodeState)S.Model.Find(id).State;
        public void Set(string id, string path, object v)
        {
            var r = S.Set(id, path, v);
            if (r == null || !r.Ok) throw new Exception($"set {path}: {r}");
        }
        public void Step(int n = 1) { for (int i = 0; i < n; i++) Rt.Tick(Dt); }
        public int Entries => S.Commands.UndoList.Count;
        public string TopLabel => S.Commands.UndoList.Count > 0 ? S.Commands.UndoList[0].Label : null;
        public void Dispose() => Rt.Dispose();
    }

    static GameObject camGo;

    /// <summary>Dynamic bones and bust physics off (they carry history, so poses are compared without them).</summary>
    static void NoPhysics(CharaRealization r)
    {
        var m = r?.Motion;
        if (!m) return;
        foreach (var d in m.GetAll<HS2Next.Motion.DynamicBones.DynamicBoneStage>()) d.Enabled = false;
        foreach (var b in m.GetAll<HS2Next.Motion.DynamicBones.BustStage>()) b.Enabled = false;
    }

    static Transform Bone(CharaRealization r, string name) => r?.Context?.BodyBones != null && r.Context.BodyBones.TryGetValue(name, out var t) ? t : null;
    static Vector3 U(Vec3 v) => new Vector3(v.X, v.Y, v.Z);
    static string F(Vector3 v) => $"({v.x:G6}, {v.y:G6}, {v.z:G6})";
    static float Angle(Quaternion a, Quaternion b)
    {
        var d = Quaternion.Inverse(a) * b;
        double v = Math.Sqrt((double)d.x * d.x + (double)d.y * d.y + (double)d.z * d.z);
        return (float)(2 * Math.Atan2(v, Math.Abs((double)d.w)) * 180 / Math.PI);
    }
    static bool Bits(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
    static bool Bits(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
    static string Few(List<string> l) => l.Count == 0 ? "" : $"{l.Count}: " + string.Join(", ", l.Take(8)) + (l.Count > 8 ? ", ..." : "");

    /// <summary>Local pose of every transform under the character root (stops at scene node holders).</summary>
    static List<(Transform t, Vector3 p, Quaternion q)> Snap(CharaRealization r)
    {
        var l = new List<(Transform, Vector3, Quaternion)>();
        void Walk(Transform t)
        {
            l.Add((t, t.localPosition, t.localRotation));
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (SceneNodeHolder.IsHolder(c)) continue;
                Walk(c);
            }
        }
        Walk(r.Root.transform);
        return l;
    }

    static List<string> SnapDiff(List<(Transform t, Vector3 p, Quaternion q)> a)
    {
        var bad = new List<string>();
        foreach (var (t, p, q) in a)
            if (t && (!Bits(t.localPosition, p) || !Bits(t.localRotation, q))) bad.Add($"{t.name} {Angle(t.localRotation, q):G3}° {(t.localPosition - p).magnitude:G3}");
        return bad;
    }

    static List<string> SnapDiff(List<(Transform t, Vector3 p, Quaternion q)> a, List<(Transform t, Vector3 p, Quaternion q)> b)
    {
        var bad = new List<string>();
        int n = Math.Min(a.Count, b.Count);
        if (a.Count != b.Count) bad.Add($"count {a.Count} vs {b.Count}");
        for (int i = 0; i < n; i++)
            if (a[i].t.name != b[i].t.name || !Bits(a[i].p, b[i].p) || !Bits(a[i].q, b[i].q)) bad.Add(a[i].t.name);
        return bad;
    }

    static readonly string[] LimbBones = { "cf_J_ArmUp00_L", "cf_J_ArmLow01_L", "cf_J_Hand_L", "cf_J_ArmUp00_R", "cf_J_ArmLow01_R", "cf_J_Hand_R",
                                           "cf_J_LegUp00_L", "cf_J_LegLow01_L", "cf_J_Foot01_L", "cf_J_LegUp00_R", "cf_J_LegLow01_R", "cf_J_Foot01_R" };
    static readonly int[] EndTargets = { 3, 6, 9, 12 };

    /// <summary>Largest distance of a hand / foot from its effective target (world), over the applied limbs.</summary>
    static float EndResidual(IkCharacter c, out string worst)
    {
        float m = 0; worst = "";
        foreach (int i in EndTargets)
        {
            var g = IkGroupInfo.GroupOf((IkTargetId)i);
            if (!c.Applied(g)) continue;
            var tv = c.Effective((IkTargetId)i);
            var src = c.Bones.Sources[i];
            if (!tv.Has || !src) continue;
            float d = (src.position - c.Root.transform.TransformPoint(tv.Pos)).magnitude;
            if (d > m) { m = d; worst = IkGroupInfo.TargetNames[i]; }
        }
        return m;
    }

    static bool Reachable(IkCharacter c)
    {
        foreach (int i in EndTargets)
        {
            var l = c.Bones.Limb(IkGroupInfo.GroupOf((IkTargetId)i));
            if (l == null || !l.Available) continue;
            int ri = i - 2;
            var a = c.Effective((IkTargetId)ri); var e = c.Effective((IkTargetId)i);
            if (!a.Has || !e.Has) continue;
            float L = (l.Mid.position - l.Root.position).magnitude + (l.End.position - l.Mid.position).magnitude;
            if ((c.Root.transform.TransformPoint(e.Pos) - c.Root.transform.TransformPoint(a.Pos)).magnitude > 0.999f * L) return false;
        }
        return true;
    }

    static string PosePath(string rel) => Path.Combine(poseRoot, rel.Replace('/', Path.DirectorySeparatorChar));

    // ================================================================ D1 build

    static void D1()
    {
        Section("D1", "build: IkBones, IkStage placement, registration");
        Check("CharaPoseDrivers.Ik is the IkDriver", CharaPoseDrivers.Ik is IkDriver);
        using (var e = new Env())
        {
            foreach (var (card, who) in new[] { (femaleCard, "BR-Chan"), (maleCard, "HERO107") })
            {
                var id = e.Add(card);
                var c = e.IkC(id);
                Check($"{who}: IkCharacter after the build", c != null);
                if (c == null) continue;
                Check($"{who}: every IK bone found, all five groups available", c.Bones.Missing.Count == 0 && c.Bones.GroupAvailable.All(x => x), string.Join(", ", c.Bones.Missing));
                var st = e.C(id).Motion.Stages.ToList();
                int ik = st.IndexOf(c.Stage);
                int hips = st.FindIndex(s => s is FkHipsStage), gate = st.FindIndex(s => s is FkNeckGate);
                int look = st.FindIndex(s => (int)s.Phase >= (int)MotionPhase.NeckLook);
                Check($"{who}: IkStage at phase 200 after the FK hips stage and gate, before the neck look", c.Stage.Phase == MotionPhase.Ik && ik > hips && ik > gate && hips >= 0 && gate >= 0 && (look < 0 || ik < look),
                      $"ik {ik}, hips {hips}, gate {gate}, first ≥ NeckLook {look}");
                Check($"{who}: IK off at build: stage disabled, nothing owned", !c.Stage.Enabled && c.Owned.Count == 0);
            }
        }
    }

    // ================================================================ D2 routing

    static void D2()
    {
        Section("D2", "routing of ik.enabled / groups / targets; undo restores the realized pose");
        using (var e = new Env())
        {
            var id = e.AddStill(femaleCard);
            var c = e.IkC(id);
            e.Set(id, "chara.ik.enabled", true); e.Rt.ApplyNow();
            Check("ik.enabled → the five groups applied, stage enabled", Enumerable.Range(0, 5).All(g => c.Applied((IkGroup)g)) && c.Stage.Enabled);
            e.Set(id, "chara.ik.groups.leftArm", false); e.Rt.ApplyNow();
            Check("groups.leftArm false → only LeftArm de-applied", !c.Applied(IkGroup.LeftArm) && c.Applied(IkGroup.RightArm) && c.Applied(IkGroup.Body) && c.Applied(IkGroup.LeftLeg) && c.Applied(IkGroup.RightLeg));
            var before = Enumerable.Range(0, 13).Select(i => c.Effective((IkTargetId)i)).ToArray();
            e.Set(id, "chara.ik.targets.LeftHand", new IkTarget { Pos = new Vec3(1, 2, 3), Rot = new Vec3(10, 20, 30) }); e.Rt.ApplyNow();
            var lh = c.Effective(IkTargetId.LeftHand);
            bool othersSame = Enumerable.Range(0, 13).Where(i => i != 3).All(i => before[i].Has == c.Effective((IkTargetId)i).Has && Bits(before[i].Pos, c.Effective((IkTargetId)i).Pos));
            Check("a target path sets that target only (model value, no snapshot)", lh.Has && !lh.Snapshot && lh.Pos == new Vector3(1, 2, 3) && lh.Rot == new Vector3(10, 20, 30) && othersSame, lh.ToString());
            e.Set(id, "chara.ik.targets.LeftHand", null); e.Rt.ApplyNow();
            lh = c.Effective(IkTargetId.LeftHand);
            Check("removing the value: the target is missing or a runtime snapshot", !lh.Has || lh.Snapshot, lh.ToString());
            e.Set(id, "chara.ik.enabled", false); e.Rt.ApplyNow();
            Check("ik.enabled false → nothing applied, stage disabled", Enumerable.Range(0, 5).All(g => !c.Applied((IkGroup)g)) && !c.Stage.Enabled);
            e.Set(id, "chara.ik.groups.leftArm", true); e.Rt.ApplyNow();
            // undo restores the realized pose
            e.Step(2);
            var baseline = Snap(e.C(id));
            int n0 = e.Entries;
            var r1 = e.Copies.IkFromAnimation(id);
            var r2 = e.Ik.SetEnabled(id, true);
            var rh = e.St(id).Ik.Targets[6];
            var r3 = e.Ik.SetTargets(id, new Dictionary<IkTargetId, IkTargetEdit> { [IkTargetId.RightHand] = new IkTargetEdit(U(rh.Pos) + new Vector3(-1f, 1.5f, 1f), null) });
            e.Step(3);
            var moved = SnapDiff(baseline);
            Check("three entries; the IK pose differs from the clip", r1.Ok && r2.Ok && r3.Ok && e.Entries == n0 + 3 && moved.Count > 0, $"{e.Entries - n0} entries; {moved.Count} transforms moved");
            for (int i = 0; i < 3; i++) e.S.Commands.Undo();
            e.Rt.ApplyNow();
            e.Step(2);
            var diff = SnapDiff(baseline);
            Check("undo ×3 → after 2 steps every transform equals the pre-IK pose (bit-equal)", diff.Count == 0, Few(diff));
        }
    }

    // ================================================================ D3 capture rule

    static void D3()
    {
        Section("D3", "capture rule");
        using (var e = new Env())
        {
            var id = e.AddStill(femaleCard);
            var c = e.IkC(id);
            var probe = c.AnimationPose();
            int n0 = e.Entries;
            var sw = Stopwatch.StartNew();
            var r = e.Ik.SetEnabled(id, true);
            double ms = sw.Elapsed.TotalMilliseconds;
            var st = e.St(id);
            float dp = 0, dr = 0;
            for (int i = 0; i < 13; i++)
            {
                var t = st.Ik.Targets[i];
                if (t == null) { dp = float.PositiveInfinity; continue; }
                dp = Mathf.Max(dp, (U(t.Pos) - probe[i].Pos).magnitude);
                dr = Mathf.Max(dr, Angle(Quaternion.Euler(U(t.Rot)), Quaternion.Euler(probe[i].Rot)));
            }
            Check("SetEnabled on a fresh character: one entry \"IK\", 13 targets = the probe pose (1e-5, 0.01°)", r.Ok && e.Entries == n0 + 1 && e.TopLabel == "IK" && dp <= 1e-5f && dr <= 0.01f && e.Ik.LastCaptured.Count == 13,
                  $"{r}; pos {dp:G3}, rot {dr:G3}°, captured {e.Ik.LastCaptured.Count}, label {e.TopLabel}");
            Measure($"U1 IkOps.SetEnabled with a 13-target capture (probe): {ms:F3} ms");
            bool rotZero = new[] { 0, 1, 2, 4, 5, 7, 8, 10, 11 }.All(i => st.Ik.Targets[i] != null && st.Ik.Targets[i].Rot.Equals(Vec3.Zero));
            Check("non-hand / foot targets store rot 0; Body = cf_J_Hips position", rotZero && (U(st.Ik.Targets[0].Pos) - e.C(id).Root.transform.InverseTransformPoint(Bone(e.C(id), "cf_J_Hips").position)).magnitude <= 1e-4f);
            e.S.Commands.Undo(); e.Rt.ApplyNow();
            st = e.St(id);
            Check("undo removes the captured targets and the flag", !st.Ik.Enabled && st.Ik.Targets.Targets.All(x => x == null));
            // a group switch captures its targets only
            e.Set(id, "chara.ik.enabled", true);
            foreach (var g in IkGroupInfo.MemberNames) e.Set(id, "chara.ik.groups." + g, false);
            e.Rt.ApplyNow();
            var r2 = e.Ik.SetGroup(id, IkGroup.LeftArm, true);
            st = e.St(id);
            var set = Enumerable.Range(0, 13).Where(i => st.Ik.Targets[i] != null).Select(i => IkTargets.Names[i]).ToList();
            Check("SetGroup(LeftArm) captures LeftShoulder, LeftArmChain, LeftHand only", r2.Ok && set.SequenceEqual(new[] { "LeftShoulder", "LeftArmChain", "LeftHand" }), string.Join(",", set));
            // SetTargets: the edited value plus every other missing target
            var r3 = e.Ik.SetTargets(id, new Dictionary<IkTargetId, IkTargetEdit> { [IkTargetId.RightFoot] = new IkTargetEdit(new Vector3(0.5f, 1f, 0.3f), null) });
            st = e.St(id);
            Check("SetTargets: one entry \"IK targets\"; RightFoot = the edit (rot kept from the capture); all 13 present", r3.Ok && e.TopLabel == "IK targets" && st.Ik.Targets.Targets.All(x => x != null) && U(st.Ik.Targets[12].Pos) == new Vector3(0.5f, 1f, 0.3f));
            // world space round trip
            var root = e.C(id).Root.transform;
            var wpos = root.TransformPoint(new Vector3(0.2f, 9f, 1f));
            var r4 = e.Ik.SetTargets(id, new Dictionary<IkTargetId, IkTargetEdit> { [IkTargetId.LeftHand] = new IkTargetEdit(wpos, new Vector3(10, 20, 30)) }, IkSpace.World);
            st = e.St(id);
            Check("SetTargets world space → root space (1e-5)", r4.Ok && (U(st.Ik.Targets[3].Pos) - new Vector3(0.2f, 9f, 1f)).magnitude <= 1e-5f, F(U(st.Ik.Targets[3].Pos)));
            // not built
            var nb = e.S.AddChara(null);
            if (nb != null && nb.Ok)
            {
                e.Rt.ApplyNow();
                var r5 = e.Ik.SetEnabled(nb.NewId, true);
                var st5 = e.St(nb.NewId);
                Check("not built: the flag only, ik.captureUnavailable", r5.Ok && st5.Ik.Enabled && st5.Ik.Targets.Targets.All(x => x == null) && e.S.Findings.Any(f => f.Code == "ik.captureUnavailable"), r5.ToString());
                var r6 = e.Copies.IkFromAnimation(nb.NewId);
                Check("not built: a copy fails (unavailable), nothing written, ik.copyUnavailable", !r6.Ok && e.St(nb.NewId).Ik.Targets.Targets.All(x => x == null) && e.S.Findings.Any(f => f.Code == "ik.copyUnavailable"), r6.ToString());
            }
            else Info("AddChara(null) refused: not-built case skipped (" + nb + ")");
        }
        // vanilla parity: the first-frame copy
        IkOptions.MissingTargetsFirstFrame = true;
        using (var e = new Env())
        {
            var id = e.Add(femaleCard);
            e.S.SetAnimation(id, Idle); e.Rt.ApplyNow();
            e.Step(1);
            var c = e.IkC(id);
            var first = c.FirstFrame;
            e.Step(20);
            var r = e.Ik.SetEnabled(id, true);
            var st = e.St(id);
            bool eq = first != null && Enumerable.Range(0, 13).All(i => st.Ik.Targets[i] != null && Bits(U(st.Ik.Targets[i].Pos), first[i].Pos));
            float moved = first == null ? 0 : (c.CurrentPose()[3].Pos - first[3].Pos).magnitude;
            Check("MissingTargetsFirstFrame: the capture writes the first frame's values (not the current pose)", r.Ok && eq, $"first frame {(first != null ? "kept" : "missing")}; the left hand moved {moved:G3} since");
        }
    }

    // ================================================================ D4 copies

    static void D4()
    {
        Section("D4", "copies: animation → FK / IK = the probe, current copies = the final pose, no mode switched");
        camGo = camGo ? camGo : new GameObject("ik_test_camera") { hideFlags = HideFlags.HideAndDontSave };
        camGo.transform.position = new Vector3(6f, 14f, 6f);
        LookTargets.DefaultCamera = camGo.transform;
        using (var e = new Env())
        {
            var id = e.AddStill(femaleCard);
            e.Set(id, "chara.hands", new[] { 3, 3 });
            e.Set(id, "chara.look.neck", 1);   // camera
            e.Rt.ApplyNow();
            e.Step(90);
            var r = e.C(id);
            var fkc = e.FkC(id);
            var neck = Bone(r, "cf_J_Neck");
            // reference: the probe read directly
            var refRot = new Dictionary<string, Quaternion>();
            var bones = FkOps.AnimationGroups.SelectMany(g => fkc.Table.Of(g)).Where(b => b.Bone && !b.Additional).ToList();
            r.Motion.Probe(MotionPhase.IkPrepare, r.Root.transform, () => { foreach (var b in bones) refRot[b.Key] = b.Bone.localRotation; });
            int n0 = e.Entries;
            var sw = Stopwatch.StartNew();
            var k = e.Copies.FkFromAnimation(id);
            Measure($"U1 KinematicCopies.FkFromAnimation (probe + {k.Copied.Count} bones): {sw.Elapsed.TotalMilliseconds:F3} ms");
            var st = e.St(id);
            float worst = 0; string worstKey = "";
            foreach (var b in bones)
            {
                if (!st.Fk.Bones.TryGetValue(b.Key, out var v)) { worst = float.PositiveInfinity; worstKey = b.Key + " missing"; continue; }
                float a = Angle(Quaternion.Euler(U(v)), refRot[b.Key]);
                if (a > worst) { worst = a; worstKey = b.Key; }
            }
            Check("FK from animation: one entry \"FK from animation\", values = the probe (≤ 0.01°), FK stays off", k.Ok && e.Entries == n0 + 1 && e.TopLabel == "FK from animation" && worst <= 0.01f && !st.Fk.Enabled && !st.Ik.Enabled,
                  $"{k}; {k.Copied.Count} bones, worst {worst:G3}° {worstKey}");
            var finger = bones.FirstOrDefault(b => b.Group == FkGroup.RightHand && b.Bone.name.Contains("Index"));
            if (finger != null)
                Check("hand pattern included: a finger value = its current (patterned) rotation (≤ 0.01°)", Angle(Quaternion.Euler(U(st.Fk.Bones[finger.Key])), finger.Bone.localRotation) <= 0.01f, finger.Key);
            float neckVsFinal = neck ? Angle(Quaternion.Euler(U(st.Fk.Bones.TryGetValue("cf_J_Neck", out var nv) ? nv : Vec3.Zero)), neck.localRotation) : 0f;
            Info($"neck: copied value vs the final (camera-look) neck {neckVsFinal:G3}°");
            Check("neck look excluded: the copied neck is the clip's (differs from the camera-look final neck by > 0.5°)", neckVsFinal > 0.5f, $"{neckVsFinal:G3}°");
            var bad = e.Copies.FkFromAnimation(id, new[] { FkGroup.Hair });
            Check("FK from animation with Hair → bad_args", !bad.Ok && bad.Command.Code == CommandCodes.BadArgs);
            // IK from animation / current
            var c = e.IkC(id);
            var probe = c.AnimationPose();
            sw.Restart();
            var ki = e.Copies.IkFromAnimation(id);
            Measure($"U1 KinematicCopies.IkFromAnimation (13 targets): {sw.Elapsed.TotalMilliseconds:F3} ms");
            st = e.St(id);
            float dp = Enumerable.Range(0, 13).Max(i => st.Ik.Targets[i] == null ? float.PositiveInfinity : (U(st.Ik.Targets[i].Pos) - probe[i].Pos).magnitude);
            Check("IK from animation: \"IK from animation\", 13 targets = the probe (1e-6), IK stays off", ki.Ok && e.TopLabel == "IK from animation" && ki.Copied.Count == 13 && dp <= 1e-6f && !st.Ik.Enabled, $"{ki}; {dp:G3}");
            var lhT = e.St(id).Ik.Targets[3];
            e.Ik.SetTargets(id, new Dictionary<IkTargetId, IkTargetEdit> { [IkTargetId.LeftHand] = new IkTargetEdit(U(lhT.Pos) + new Vector3(0.3f, 0.3f, 0.3f), null) });
            var cur = c.CurrentPose();
            var kc = e.Copies.IkFromCurrent(id, new[] { IkGroup.LeftArm });
            st = e.St(id);
            float dc = new[] { 1, 2, 3 }.Max(i => (U(st.Ik.Targets[i].Pos) - cur[i].Pos).magnitude);
            float dcr = Angle(Quaternion.Euler(U(st.Ik.Targets[3].Rot)), Quaternion.Euler(cur[3].Rot));
            Check("IK from current (LeftArm): \"IK from pose\", the 3 targets = the final pose (1e-5, 0.01°)", kc.Ok && e.TopLabel == "IK from pose" && kc.Copied.SequenceEqual(new[] { "LeftShoulder", "LeftArmChain", "LeftHand" }) &&
                  dc <= 1e-5f && dcr <= 0.01f, $"{string.Join(",", kc.Copied)}; pos {dc:G3}, rot {dcr:G3}°");
            var kf = e.Copies.FkFromCurrent(id);
            st = e.St(id);
            float nfin = neck ? Angle(Quaternion.Euler(U(st.Fk.Bones["cf_J_Neck"])), neck.localRotation) : 0;
            Check("FK from current: \"FK from pose\", the neck = the final (camera-look) neck (≤ 0.01°)", kf.Ok && e.TopLabel == "FK from pose" && nfin <= 0.01f && !st.Fk.Enabled, $"{nfin:G3}°");
            // U1 warmed costs (mean of 20 calls each)
            sw.Restart(); for (int i = 0; i < 20; i++) c.AnimationPose();
            double tProbe = sw.Elapsed.TotalMilliseconds / 20;
            sw.Restart(); for (int i = 0; i < 20; i++) c.PoseWithoutIk();
            double tCap = sw.Elapsed.TotalMilliseconds / 20;
            sw.Restart(); for (int i = 0; i < 20; i++) new FkOps(e.Rt).AnimationValues(id, null);
            double tFk = sw.Elapsed.TotalMilliseconds / 20;
            sw.Restart(); for (int i = 0; i < 20; i++) e.Copies.IkFromCurrent(id);
            double tCopy = sw.Elapsed.TotalMilliseconds / 20;
            Measure($"U1 warmed (mean of 20): probe read of the 13 sources {tProbe:F3} ms, capture source (probe + FK) {tCap:F3} ms, FK animation values {tFk:F3} ms, IkFromCurrent with its transaction {tCopy:F3} ms");
            var ke = e.Copies.IkFromAnimation(id, null, true);
            st = e.St(id);
            Check("enable = true: one entry switching IK on (FK untouched without the preset)", ke.Ok && st.Ik.Enabled && !st.Fk.Enabled);
        }
    }

    // ================================================================ D5 bake (U7)

    static void D5()
    {
        Section("D5", "bake: IK pose → FK + hipsPosition; one step later every body bone within 1e-4 / 0.01° (U7 on P1–P8)");
        int done = 0;
        foreach (var rel in ComparePoses)
        {
            var path = PosePath(rel);
            if (!File.Exists(path)) { Info("missing: " + rel); continue; }
            using (var e = new Env())
            {
                var id = e.AddStill(femaleCard);
                var rep2 = PoseImport.ApplyFile(e.Rt, id, path);
                e.Set(id, "chara.anime.speed", 0f); e.Rt.ApplyNow();
                e.Step(3);
                var r = e.C(id);
                var fkc = e.FkC(id); var c = e.IkC(id);
                var watch = fkc.Table.Of(FkGroup.Body).Where(b => b.Bone).Select(b => b.Bone).Concat(c.Owned).Distinct().ToList();
                var before = watch.ToDictionary(t => t, t => (t.localPosition, t.localRotation));
                var hips = Bone(r, "cf_J_Hips");
                var hp = hips.localPosition;
                int n0 = e.Entries;
                var k = e.Copies.Bake(id);
                e.Step(1);
                float ang = 0, pos = 0; string worst = "";
                foreach (var kv in before)
                {
                    float a = Angle(kv.Key.localRotation, kv.Value.localRotation);
                    if (a > ang) { ang = a; worst = kv.Key.name; }
                }
                pos = (hips.localPosition - hp).magnitude;
                var st = e.St(id);
                bool flags = !st.Ik.Enabled && st.Fk.Enabled && st.Fk.Groups.Body && st.Fk.HipsPosition.HasValue && st.Fk.HipsOffset == 0f;
                Check($"{rel}: bake (one entry \"Bake IK\", IK off, FK + Body on, hipsPosition) and one step: body bones ≤ 0.01°, hips ≤ 1e-4",
                      rep2.Applied && k.Ok && e.Entries == n0 + 1 && e.TopLabel == "Bake IK" && flags && ang <= 0.01f && pos <= 1e-4f,
                      $"import ×{rep2.IkScale}; bake {k} {k.Bones} bones; max {ang:G3}° ({worst}), hips {pos:G3}; IK owned {c.Owned.Count} before");
                Measure($"U7 {rel}: max body-bone angle {ang:G3}° ({worst}), hips {pos:G3} units after bake + 1 step");
                done++;
            }
        }
        Check("at least one comparison pose present", done > 0, done + " of 8");
    }

    // ================================================================ D6 exclusive preset

    static void D6()
    {
        Section("D6", "exclusive preset");
        using (var e = new Env())
        {
            var id = e.AddStill(femaleCard);
            e.Ik.SetEnabled(id, true);
            var r = e.Fk.SetEnabled(id, true);
            Check("preset off (default): FK on keeps IK on", r.Ok && e.St(id).Fk.Enabled && e.St(id).Ik.Enabled);
            e.Fk.SetEnabled(id, false);
            IkOptions.Exclusive = true;
            int n0 = e.Entries;
            r = e.Fk.SetEnabled(id, true);
            Check("preset on: FK on switches IK off in the same entry", r.Ok && e.St(id).Fk.Enabled && !e.St(id).Ik.Enabled && e.Entries == n0 + 1);
            n0 = e.Entries;
            r = e.Ik.SetEnabled(id, true);
            Check("preset on: IK on switches FK off in the same entry", r.Ok && !e.St(id).Fk.Enabled && e.St(id).Ik.Enabled && e.Entries == n0 + 1);
            e.S.Commands.Undo(); e.Rt.ApplyNow();
            Check("undo restores both flags", e.St(id).Fk.Enabled && !e.St(id).Ik.Enabled);
            e.Set(id, "chara.fk.enabled", true); e.Set(id, "chara.ik.enabled", true); e.Rt.ApplyNow();
            Check("raw sets (file data) are not affected by the preset", e.St(id).Fk.Enabled && e.St(id).Ik.Enabled);
        }
    }

    // ================================================================ D7 suspend

    static void D7()
    {
        Section("D7", "suspend under StudioDefaultPoseScope");
        using (var e = new Env())
        {
            var a = e.AddStill(femaleCard); var b = e.AddStill(femaleCard);
            e.Copies.IkFromAnimation(a);
            var t = e.St(a).Ik.Targets[6];
            e.Ik.SetTargets(a, new Dictionary<IkTargetId, IkTargetEdit> { [IkTargetId.RightHand] = new IkTargetEdit(U(t.Pos) + new Vector3(-1f, 1.5f, 1f), null) });
            e.Ik.SetEnabled(a, true);
            e.Step(3);
            var ik = Snap(e.C(a));
            var twin = Snap(e.C(b));
            var hand = Bone(e.C(a), "cf_J_ArmUp00_R"); var handB = Bone(e.C(b), "cf_J_ArmUp00_R");
            Quaternion inA, inB;
            using (StudioDefaultPoseScope.Enter(e.C(a)))
            using (StudioDefaultPoseScope.Enter(e.C(b)))
            {
                inA = hand.localRotation; inB = handB.localRotation;
                Check("inside the scope the IK stage is suspended", e.IkC(a).Suspended && e.IkC(a).Stage.Suspended);
            }
            Check("inside the scope the arm shows the default pose (= a twin without IK, bit-equal)", Bits(inA, inB), $"{Angle(inA, inB):G3}°");
            e.Step(1);
            var twinDrift = new HashSet<string>(SnapDiff(twin).Select(x => x.Split(' ')[0]));
            var diffAll = SnapDiff(ik);
            var diff = diffAll.Where(x => !twinDrift.Contains(x.Split(' ')[0])).ToList();
            Info($"through the scope a twin without IK changes {twinDrift.Count} transforms ({string.Join(", ", twinDrift.Take(6))}); the IK character {diffAll.Count}");
            Check("after Dispose and one step the IK pose is back (bit-equal, except what the scope changes on a twin without IK)", diff.Count == 0, Few(diff));
        }
    }

    // ================================================================ D8 FK → IK round trip

    static void D8()
    {
        Section("D8", "the 15 FK compare poses: FK → IK copy → every IK group on, FK off, IK on → end effectors, thigh roots, limb local angles");
        var table = JObject.Parse(File.ReadAllText(Path.Combine(ContentSettings.ProjectRoot, "Tools", "compare_poses.json")));
        var all = new List<float>();
        var perBone = LimbBones.ToDictionary(x => x, x => new List<float>());
        var posErr = LimbBones.ToDictionary(x => x, x => new List<float>());
        int n = 0;
        foreach (var p in (JArray)table["poses"])
        {
            var path = PosePath((string)p["path"]);
            if (!File.Exists(path)) { Info("missing " + p["path"]); continue; }
            using (var e = new Env())
            {
                var id = e.AddStill(femaleCard);
                var rp = PoseImport.ApplyFile(e.Rt, id, path);
                e.Set(id, "chara.anime.speed", 0f); e.Rt.ApplyNow();
                e.Step(2);
                var r = e.C(id);
                var before = LimbBones.Select(x => Bone(r, x).localRotation).ToArray();
                var beforeW = LimbBones.Select(x => r.Root.transform.InverseTransformPoint(Bone(r, x).position)).ToArray();
                var k = e.Copies.IkFromCurrent(id);
                e.Ik.SetGroups(id, KinematicCopies.AllIkGroups, true);   // a file's stored Body-off would hide the hips turn
                e.Fk.SetEnabled(id, false);
                e.Ik.SetEnabled(id, true);
                e.Step(1);
                var ang = LimbBones.Select((x, i) => Angle(Bone(r, x).localRotation, before[i])).ToList();
                all.AddRange(ang);
                for (int i = 0; i < LimbBones.Length; i++)
                {
                    perBone[LimbBones[i]].Add(ang[i]);
                    posErr[LimbBones[i]].Add((r.Root.transform.InverseTransformPoint(Bone(r, LimbBones[i]).position) - beforeW[i]).magnitude);
                }
                var s = ang.OrderBy(x => x).ToList();
                Info($"case {p["case"]} {p["path"]}: median {s[s.Count / 2]:G3}°, max {s[s.Count - 1]:G3}° ({LimbBones[ang.IndexOf(s[s.Count - 1])]}); copy {k}; residual {EndResidual(e.IkC(id), out var w):G3} {w}");
                n++;
            }
        }
        if (all.Count == 0) { Check("compare poses present", false); return; }
        foreach (var b in LimbBones)
        {
            var a = perBone[b].OrderBy(x => x).ToList(); var q = posErr[b].OrderBy(x => x).ToList();
            Info($"{b}: local angle median {a[a.Count / 2]:G3}°, max {a[a.Count - 1]:G3}°; root-space position median {q[q.Count / 2]:G3}, max {q[q.Count - 1]:G3}");
        }
        var sorted = all.OrderBy(x => x).ToList();
        float med = sorted[sorted.Count / 2], max = sorted[sorted.Count - 1];
        float p95 = sorted[(int)(sorted.Count * 0.95)];
        var ends = new[] { "cf_J_Hand_L", "cf_J_Hand_R", "cf_J_Foot01_L", "cf_J_Foot01_R" }.SelectMany(b => posErr[b]).OrderBy(x => x).ToList();
        var thighs = new[] { "cf_J_LegUp00_L", "cf_J_LegUp00_R" }.SelectMany(b => posErr[b]).OrderBy(x => x).ToList();
        // gate accepted by the integrator after A's fix round (bend-plane limbs, K25): the spec's 0.5° / 2° is not reachable
        Check($"{n} poses: hands / feet back on the FK pose positions (median ≤ 1e-4)", ends[ends.Count / 2] <= 1e-4f, $"median {ends[ends.Count / 2]:G3}, max {ends[ends.Count - 1]:G3}");
        Check($"{n} poses: thigh roots (LegUp00 root-space position) vs the FK pose max ≤ 0.15", thighs[thighs.Count - 1] <= 0.15f, $"median {thighs[thighs.Count / 2]:G3}, max {thighs[thighs.Count - 1]:G3}");
        Check($"{n} poses: limb local angles vs the FK pose median ≤ 10° (p95 / max reported)", med <= 10f, $"median {med:G3}°, p95 {p95:G3}°, max {max:G3}°");
        Measure($"D8 round trip over {n} poses: limb local angles median {med:G3}°, p95 {p95:G3}°, max {max:G3}°; thigh roots max {thighs[thighs.Count - 1]:G3}; hands / feet median {ends[ends.Count / 2]:G3}");
    }

    // ================================================================ D9 animation under IK

    static void D9()
    {
        Section("D9", "a playing clip under IK: effectors static, the rest follows the clip");
        using (var e = new Env())
        {
            var a = e.AddStill(femaleCard, Idle, 0f); var b = e.AddStill(femaleCard, Idle, 0f);
            foreach (var id in new[] { a, b }) e.Set(id, "chara.look.neck", 3);   // animation
            e.Copies.IkFromAnimation(a);
            e.Ik.SetEnabled(a, true);
            foreach (var id in new[] { a, b }) e.Set(id, "chara.anime.speed", 1f);
            e.Rt.ApplyNow();
            var ca = e.IkC(a);
            var follow = new[] { "cf_J_Spine03", "cf_J_Neck", "cf_J_Head" }.Select(x => (Bone(e.C(a), x), Bone(e.C(b), x))).ToList();
            var fingersA = e.FkC(a).Table.Of(FkGroup.RightHand).Where(x => x.Bone).Select(x => x.Bone).ToList();
            var fingersB = e.FkC(b).Table.Of(FkGroup.RightHand).Where(x => x.Bone).Select(x => x.Bone).ToList();
            float worst = 0; int badFollow = 0; string w = "";
            var badNames = new HashSet<string>();
            var t0 = Snap(e.C(b));
            for (int i = 0; i < 60; i++)
            {
                e.Step();
                float d = EndResidual(ca, out var wn);
                if (d > worst) { worst = d; w = wn; }
                foreach (var (x, y) in follow) if (!Bits(x.localRotation, y.localRotation)) { badFollow++; badNames.Add(x.name); }
                for (int j = 0; j < fingersA.Count && j < fingersB.Count; j++) if (!Bits(fingersA[j].localRotation, fingersB[j].localRotation)) { badFollow++; badNames.Add(fingersA[j].name); }
            }
            bool animated = SnapDiff(t0).Count > 0;
            Check("reachable effectors at their targets ≤ 1e-4 every step (60 steps at speed 1)", worst <= 1e-4f, $"{worst:G3} {w}");
            Check("Spine03 / neck / head / fingers = a twin without IK (bit-equal) every step; the clip moves", badFollow == 0 && animated, $"{badFollow} differences ({string.Join(", ", badNames.Take(10))}); clip moved {animated}");
            Info($"solve info: {(ca.Stage.LastSolve.Valid ? $"{ca.Stage.LastSolve.Milliseconds:F4} ms solver, {ca.Stage.LastSolve.StageMilliseconds:F4} ms stage" : "no solve")}");
        }
    }

    // ================================================================ D10 scale and rotation

    static void D10()
    {
        Section("D10", "character scale 1.5 and a rotated node: targets carried, solve exact");
        using (var e = new Env())
        {
            var id = e.AddStill(femaleCard);
            e.Copies.IkFromAnimation(id);
            var t = e.St(id).Ik.Targets[6];
            e.Ik.SetTargets(id, new Dictionary<IkTargetId, IkTargetEdit> { [IkTargetId.RightHand] = new IkTargetEdit(U(t.Pos) + new Vector3(-0.5f, 0.8f, 0.6f), null) });
            e.Ik.SetEnabled(id, true);
            e.Step(2);
            var c = e.IkC(id);
            float r0 = EndResidual(c, out _);
            e.Set(id, "transform.scale", new Vec3(1.5f, 1.5f, 1.5f));
            e.Set(id, "transform.rot", new Vec3(0f, 90f, 0f));
            e.Set(id, "transform.pos", new Vec3(3f, 0f, -2f));
            e.Rt.ApplyNow();
            e.Step(2);
            float r1 = EndResidual(c, out var w);
            var hand = Bone(e.C(id), "cf_J_Hand_R");
            var expect = c.Root.transform.TransformPoint(c.Effective(IkTargetId.RightHand).Pos);
            Check("targets are root-space (carried by the node); hands / feet on them ≤ 1.5e-4 after scale + rotate + move", r0 <= 1e-4f && r1 <= 1.5e-4f && (hand.position - expect).magnitude <= 1.5e-4f && Bits(U(t.Pos) + new Vector3(-0.5f, 0.8f, 0.6f), U(e.St(id).Ik.Targets[6].Pos)),
                  $"before {r0:G3}, after {r1:G3} {w}");
        }
    }

    // ================================================================ D11 round trip

    static SaveOptions FixedSave() => new SaveOptions { Clock = () => new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc), Thumbnail = ThumbnailMode.Placeholder, AppVersion = "0.1.0" };

    static void D11()
    {
        Section("D11", "save → load → save byte-identical; null targets stay null");
        using (var e = new Env())
        {
            var a = e.AddStill(femaleCard);
            e.Copies.IkFromAnimation(a);
            e.Ik.SetEnabled(a, true);
            e.Ik.SetGroup(a, IkGroup.LeftLeg, false);
            e.Set(a, "chara.fk.hipsPosition", new Vec3(0.1f, 11.2f, -0.3f));
            var b = e.AddStill(femaleCard);
            e.Set(b, "chara.ik.enabled", true);   // raw: targets stay null
            e.Rt.ApplyNow();
            e.Step(5);
            var bytes1 = e.Rt.Ops.ToBytes(FixedSave());
            Check("a scene with IK on and null targets: still null after build and 5 steps", e.St(b).Ik.Targets.Targets.All(x => x == null) && e.IkC(b).SnapshotNames().Count() > 0,
                  $"{e.IkC(b).SnapshotNames().Count()} snapshots");
            using (var e2 = new Env(0x2e2eUL))
            {
                var ld = e2.Rt.Ops.Load(bytes1);
                e2.Rt.ApplyNow();
                e2.Step(5);
                var bytes2 = e2.Rt.Ops.ToBytes(FixedSave());
                Check("save → load → 5 steps → save: bytes identical (targets, groups, fk.hipsPosition, null targets)", ld.Ok && bytes1.SequenceEqual(bytes2), $"{bytes1.Length} / {bytes2.Length} bytes");
                Check("the loaded nodes: targets / groups / hipsPosition as saved", e2.St(a).Ik.Targets.Targets.All(x => x != null) && !e2.St(a).Ik.Groups.LeftLeg && e2.St(a).Fk.HipsPosition.HasValue && e2.St(b).Ik.Targets.Targets.All(x => x == null));
            }
        }
    }

    // ================================================================ D12 determinism (U3)

    static void D12()
    {
        Section("D12", "determinism: two runtimes, the same commands, 120 steps");
        List<(Transform, Vector3, Quaternion)>[] Run1(Env e, out string id)
        {
            id = e.AddStill(femaleCard, Idle, 1f);
            e.Copies.IkFromAnimation(id);
            var t = e.St(id).Ik.Targets[3];
            e.Ik.SetTargets(id, new Dictionary<IkTargetId, IkTargetEdit> { [IkTargetId.LeftHand] = new IkTargetEdit(U(t.Pos) + new Vector3(0.6f, 1.2f, 0.8f), new Vector3(20, 30, 0)) });
            e.Ik.SetEnabled(id, true);
            e.Fk.SetGroup(id, FkGroup.Neck, true);
            e.Fk.SetEnabled(id, true);
            var snaps = new List<(Transform, Vector3, Quaternion)>[12];
            for (int i = 0; i < 120; i++)
            {
                e.Step();
                if (i % 10 == 9) snaps[i / 10] = Snap(e.C(id));
            }
            return snaps;
        }
        using (var e1 = new Env(0x77UL))
        using (var e2 = new Env(0x77UL))
        {
            var s1 = Run1(e1, out _); var s2 = Run1(e2, out _);
            int bad = 0; string first = null;
            for (int k = 0; k < 12; k++)
            {
                var d = SnapDiff(s1[k], s2[k]);
                if (d.Count > 0) { bad++; first ??= $"step {(k + 1) * 10}: {Few(d)}"; }
            }
            Check("every transform bit-equal at every 10th step (IK over FK Neck, clip playing)", bad == 0, first ?? "");
            Measure($"U3 determinism over 120 manual steps, two runtimes: {(bad == 0 ? "bit-equal" : bad + " of 12 samples differ")} (AutoStep is not available headless; the solve is pure script math)");
        }
    }

    // ================================================================ D14 pose fragments

    static void D14()
    {
        Section("D14", "HS2Next pose files through PoseFragmentOps");
        string dir = Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "pose", "fragments");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "d14" + SaveFormats.PoseExtension);
        if (File.Exists(path)) File.Delete(path);
        using (var e = new Env())
        {
            var a = e.AddStill(femaleCard);
            e.Set(a, "chara.ik.enabled", true);          // targets null → filled in the file only
            e.Set(a, "chara.fk.enabled", true);          // Neck + Body flagged, no values → filled in the file only
            e.Set(a, "chara.face.eyes", 3);
            e.Rt.ApplyNow();
            e.Step(2);
            var docBefore = e.Rt.Ops.ToBytes(FixedSave());
            int n0 = e.Entries;
            var sw = Stopwatch.StartNew();
            var sr = PoseFragmentOps.Save(e.Rt, a, path, "d14 pose", null, false, FixedSave());
            Measure($"U1 PoseFragmentOps.Save: {sw.Elapsed.TotalMilliseconds:F2} ms, {sr.Bytes} bytes");
            var docAfter = e.Rt.Ops.ToBytes(FixedSave());
            Check("save: ok, all 7 parts, missing FK values and IK targets filled in the file", sr.Ok && sr.Parts.Count == 7 && sr.FilledIk.Count == 13 && sr.FilledFk.Count > 0, $"{sr}; filled FK {sr.FilledFk.Count}, IK {sr.FilledIk.Count}");
            Check("save: the scene is unchanged (bytes) and no history entry", docBefore.SequenceEqual(docAfter) && e.Entries == n0 && e.St(a).Ik.Targets.Targets.All(x => x == null));
            Check("IsFragment by the manifest", PoseFragmentOps.IsFragment(path) && !PoseFragmentOps.IsFragment(femaleCard));
            var again = PoseFragmentOps.Save(e.Rt, a, path, "x");
            Check("save onto an existing file without overwrite → bad_args", !again.Ok && again.Code == CommandCodes.BadArgs);
            // load on the other sex
            var m = e.AddStill(maleCard);
            n0 = e.Entries;
            var lr = PoseFragmentOps.Load(e.Rt, m, path);
            var stm = e.St(m);
            var file = PoseContainer.Load(path).Value.Pose;
            bool same = stm.Ik.Targets.Targets.All(x => x != null) && stm.Ik.Enabled && stm.Fk.Enabled && stm.Face.Eyes == 3 && file.Fk.Bones.All(kv => stm.Fk.Bones.TryGetValue(kv.Key, out var v) && v.Equals(kv.Value));
            Check("load on HERO107: one entry \"Load pose\", every part set, pose.sexMismatch", lr.Applied && e.Entries == n0 + 1 && e.TopLabel == "Load pose" && lr.Parts.Count == 7 && same && lr.Findings.Has("pose.sexMismatch"), lr.ToString());
            // parts filter
            var b = e.AddStill(femaleCard);
            var animeBefore = e.St(b).Anime.Ref;
            var lr2 = PoseFragmentOps.Load(e.Rt, b, path, new[] { "face" });
            Check("parts filter [face]: only the face is set (IK / FK unchanged)", lr2.Applied && lr2.Parts.SequenceEqual(new[] { "face" }) && e.St(b).Face.Eyes == 3 && !e.St(b).Ik.Enabled && !e.St(b).Fk.Enabled && e.St(b).Anime.Ref == animeBefore, lr2.ToString());
            var lr3 = PoseFragmentOps.Load(e.Rt, b, path, new[] { "tail" });
            Check("an unknown part → bad_args", lr3.Error != null && lr3.Code == CommandCodes.BadArgs);
            sw.Restart();
            var lr4 = PoseFragmentOps.Load(e.Rt, b, path);
            Measure($"U1 PoseFragmentOps.Load (all parts): {sw.Elapsed.TotalMilliseconds:F2} ms");
            Check("full load on BR-Chan: no sex mismatch", lr4.Applied && !lr4.Findings.Has("pose.sexMismatch"));
        }
    }
}
