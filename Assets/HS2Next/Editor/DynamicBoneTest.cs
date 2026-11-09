// Batch test of the dynamic-bone stages (Runtime/Motion/DynamicBones).
// Run: -executeMethod DynamicBoneTest.Run [-card <png>] [-male <png> | -nomale] [-frames N] [-sweep N]
// Default card: <game>/UserData/chara/female/BR-Chan HS2 Rev2.png; the male checks use -male <png>, else the
// first card of <game>/UserData/chara/male (a male -card is checked as the male card too).
//   1. structure: collider responses, Ver02 count / colliders / patterns / bind-pose rest
//      lengths and LocalPosition / tip position on a fresh p_cf_anim, Ver02 step counts, derived bust
//      parameters, vanilla back hair ids 0..2, skirt pleat1 on the body skeleton, ribbon accessory, the
//      card's chains;
//   2. reference: an independent single-step implementation for a 3-particle chain vs DynamicBoneChain
//      after 1, 2, 10 steps (tolerance 1e-5);
//   3. dynamics on the card (ctx.Motion.Step(1/60) for N frames, AutoStep left on, no play loop): finite, settle,
//      lengths, collider penetration, Step(0) hold, ResetAll, 1 m teleport, 30° head turn, TimeScale 0.5;
//   4. bust softness / weight sweep, male card (chains on, bust colliders off);
//   5. performance per step (card + a 288-particle stress set), no per-step allocations, local write-back and collider
//      broad-phase equivalence; -sweep N: build and step the first N female cards (attach / motion errors, NaN).
// Report: Spike/out/motion/dynbone_report.txt (PASS / FAIL per check); exit code 1 on any FAIL.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Chara;
using HS2Next.Content;
using HS2Next.Data;
using HS2Next.IO;
using HS2Next.Motion;
using HS2Next.Motion.DynamicBones;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class DynamicBoneTest
{
    static StringBuilder R;
    static int fails, passes;
    static readonly List<Object> trash = new List<Object>();

    static string Arg(string name, string def)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    static void Check(string name, bool ok, string detail)
    {
        R.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}: {detail}");
        if (ok) passes++; else fails++;
    }

    static void Info(string s) => R.AppendLine("      " + s);

    static string F(float v) => v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
    static string F(Vector3 v) => $"({F(v.x)}, {F(v.y)}, {F(v.z)})";

    public static void Run()
    {
        R = new StringBuilder(); fails = passes = 0;
        string outDir = Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "motion");
        Directory.CreateDirectory(outDir);
        var sw = Stopwatch.StartNew();
        ContentCatalog gd = null;
        try
        {
            var head = new StringBuilder();
            gd = CardResolveTest.BuildDb(head);
            R.AppendLine("# DynamicBoneTest " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            R.Append(head);
            int frames = int.Parse(Arg("-frames", "600"));
            string card = Arg("-card", Path.Combine(CardResolveTest.GameRoot, "UserData", "chara", "female", "BR-Chan HS2 Rev2.png"));

            Section("1a collider responses");
            ColliderChecks();
            Section("1b p_cf_anim (bind pose)");
            SkeletonChecks(gd);
            Section("1c vanilla parts");
            PartChecks(gd);
            Section("2 reference single-step implementation");
            ReferenceCheck();
            Section("3 TimeScale (fixed rate)");
            TimeScaleCheck();
            Section("3 card dynamics: " + Path.GetFileName(card));
            bool mainMale = CardDynamics(gd, card, frames);
            Section("3 local write-back vs world write-back (same card, same input)");
            FastPathCheck(gd, card);
            int sweep = int.Parse(Arg("-sweep", "0"));
            if (sweep > 0) { Section($"5 sweep: first {sweep} cards of the female folder"); Sweep(gd, sweep); }
            if (!mainMale && !TestArgs.Get().Contains("-nomale"))
            {
                string male = Arg("-male", null);
                if (male == null)
                {
                    var dir = Path.Combine(CardResolveTest.GameRoot, "UserData", "chara", "male");
                    male = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.png").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault() : null;
                }
                if (male != null)
                {
                    Section("4 male card: " + Path.GetFileName(male));
                    CardDynamics(gd, male, Math.Min(frames, 240));
                }
                else Info("no male card found");
            }
        }
        catch (Exception e)
        {
            Check("exception", false, e.ToString());
        }
        foreach (var o in trash) if (o) Object.DestroyImmediate(o);
        trash.Clear();
        gd?.UnloadAll();
        R.AppendLine();
        R.AppendLine($"SUMMARY: {passes} PASS, {fails} FAIL, {sw.ElapsedMilliseconds / 1000.0:0.0} s");
        File.WriteAllText(Path.Combine(outDir, "dynbone_report.txt"), R.ToString());
        Debug.Log($"DYNBONE TEST DONE: {passes} PASS, {fails} FAIL");
        TestExit.Exit(fails > 0 ? 1 : 0);
    }

    static void Section(string s) { R.AppendLine(); R.AppendLine("## " + s); }

    static GameObject Keep(GameObject g) { trash.Add(g); return g; }

    static Dictionary<string, Transform> Bones(Transform root)
    {
        var d = new Dictionary<string, Transform>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (!d.ContainsKey(t.name)) d[t.name] = t;
        return d;
    }

    // ------------------------------------------------------------------ 1a
    static void ColliderChecks()
    {
        float err = 0f;
        void Near(Vector3 got, Vector3 want) => err = Mathf.Max(err, (got - want).magnitude);
        Vector3 x;
        // Outside sphere: pushed to R.
        x = new Vector3(0.5f, 0, 0); DynamicBoneColliders.OutsideSphere(ref x, Vector3.zero, 1f); Near(x, new Vector3(1, 0, 0));
        x = Vector3.zero; DynamicBoneColliders.OutsideSphere(ref x, Vector3.zero, 1f); Near(x, Vector3.zero);   // at the centre: not pushed
        x = new Vector3(2, 0, 0); DynamicBoneColliders.OutsideSphere(ref x, Vector3.zero, 1f); Near(x, new Vector3(2, 0, 0));
        // Inside sphere: pulled to R; negative R flips (literal).
        x = new Vector3(3, 0, 0); DynamicBoneColliders.InsideSphere(ref x, Vector3.zero, 1f); Near(x, new Vector3(1, 0, 0));
        x = new Vector3(3, 0, 0); DynamicBoneColliders.InsideSphere(ref x, Vector3.zero, -1f); Near(x, new Vector3(-1, 0, 0));
        // Capsule a=(0,0,0) b=(0,2,0): middle, below a, above b, inside.
        Vector3 a = Vector3.zero, b = new Vector3(0, 2, 0);
        x = new Vector3(0.5f, 1, 0); DynamicBoneColliders.Capsule(ref x, a, b, 1f, false); Near(x, new Vector3(1, 1, 0));
        x = new Vector3(0, -0.5f, 0); DynamicBoneColliders.Capsule(ref x, a, b, 1f, false); Near(x, new Vector3(0, -1, 0));
        x = new Vector3(0, 2.5f, 0); DynamicBoneColliders.Capsule(ref x, a, b, 1f, false); Near(x, new Vector3(0, 3, 0));
        x = new Vector3(0, 1, 0); DynamicBoneColliders.Capsule(ref x, a, b, 1f, false); Near(x, new Vector3(0, 1, 0));
        x = new Vector3(3, 1, 0); DynamicBoneColliders.Capsule(ref x, a, b, 1f, true); Near(x, new Vector3(1, 1, 0));
        Check("collider response branches", err < 1e-6f, $"max error {err:E2} over 10 cases (outside/inside sphere, capsule middle/ends/axis/inside)");

        // Legacy sphere bug: the job drops the collider's translation.
        var go = Keep(new GameObject("dbt_sphere"));
        go.transform.position = new Vector3(5, 0, 0);
        go.transform.localScale = new Vector3(2, 2, 2);
        var c = go.AddComponent<DynamicBoneCollider>();
        c.m_Center = new Vector3(1, 0, 0); c.m_Radius = 0.5f; c.m_Height = 0f; c.enabled = true;
        var legacy = DynamicBoneColliders.Evaluate(c, ColliderPath.Job, true);
        var right = DynamicBoneColliders.Evaluate(c, ColliderPath.Job, false);
        var ver02 = DynamicBoneColliders.Evaluate(c, ColliderPath.Ver02, false);
        c.enabled = false;
        var zero = DynamicBoneColliders.Evaluate(c, ColliderPath.Job, true);
        var skipped = DynamicBoneColliders.Evaluate(c, ColliderPath.MainThread, false);
        bool ok = (legacy.A - new Vector3(2, 0, 0)).magnitude < 1e-5f && (right.A - new Vector3(7, 0, 0)).magnitude < 1e-5f
                  && Mathf.Abs(legacy.Radius - 1f) < 1e-6f && (ver02.A - right.A).magnitude < 1e-6f
                  && zero.Kind == ColliderShapeKind.Sphere && zero.A == Vector3.zero && zero.Radius == 0f && skipped.Kind == ColliderShapeKind.None;
        Check("legacy sphere / zero record", ok, $"legacy centre {F(legacy.A)} (want (2,0,0)), corrected {F(right.A)} (want (7,0,0)), radius {F(legacy.Radius)}, disabled -> {zero}, main-thread disabled -> {skipped.Kind}");
    }

    // ------------------------------------------------------------------ 1b
    static GameObject FreshAnim(ContentCatalog gd, out GameObject prefab)
    {
        prefab = gd.LoadAsset<GameObject>("studio/base/00.unity3d", "p_cf_anim") ?? gd.LoadAsset<GameObject>("chara/oo_base.unity3d", "p_cf_anim");
        var a = Keep(Object.Instantiate(prefab));
        a.name = "dbt_p_cf_anim";
        var an = a.GetComponent<Animator>(); if (an) an.enabled = false;
        a.transform.position = Vector3.zero; a.transform.rotation = Quaternion.identity;
        return a;
    }

    static void SkeletonChecks(ContentCatalog gd)
    {
        var anim = FreshAnim(gd, out var prefab);
        var vers = anim.GetComponentsInChildren<DynamicBone_Ver02>(true);
        var jRoot = CharaAssembler.Find(anim.transform, "cf_J_Root");
        var cols = jRoot.GetComponentsInChildren<DynamicBoneCollider>(true);
        int capsules = cols.Count(c => c.m_Height * 0.5f - c.m_Radius > 0 && c.enabled && c.m_Bound == 0);
        Check("Ver02 count", vers.Length == 4, $"{vers.Length} ({string.Join(", ", vers.Select(v => v.Comment))})");
        Check("body colliders", cols.Length == 21 && capsules == 21, $"{cols.Length} DynamicBoneCollider under cf_J_Root, {capsules} enabled Outside capsules");
        var spine3 = cols.FirstOrDefault(c => c.name == "cf_J_Spine03_s");
        if (spine3) Check("collider cf_J_Spine03_s", spine3.m_Direction == 0 && (spine3.m_Center - new Vector3(0, 0.7f, -0.2f)).magnitude < 1e-5f && Mathf.Abs(spine3.m_Radius - 0.8f) < 1e-6f && Mathf.Abs(spine3.m_Height - 3f) < 1e-6f,
            $"dir {spine3.m_Direction} centre {F(spine3.m_Center)} r {F(spine3.m_Radius)} h {F(spine3.m_Height)}");

        // Header + pattern values.
        var mL = vers.FirstOrDefault(v => v.Comment == "Mune_L");
        var sL = vers.FirstOrDefault(v => v.Comment == "Siri_L");
        if (mL)
        {
            var p0 = mL.Patterns[0];
            float[,] want = { { 0, 1.0f, 1.0f, 1.0f, 1.0f, 1, 0, 0 }, { 0, 0.1f, 0.2f, 0.1f, 0.8f, 1, 0, 0 }, { 1, 0.2f, 0.4f, 0.7f, 0.8f, 3, 0, 0 }, { 1, 0.3f, 0.3f, 0.5f, 0.8f, 4, 0, 0 }, { 0, 0.4f, 0.2f, 0.3f, 0.7f, 2, -0.1f, 0.1f } };
            float e = 0f;
            for (int i = 0; i < 5; i++)
            {
                var q = p0.Params[i];
                float[] got = { q.IsRotationCalc, q.Damping, q.Elasticity, q.Stiffness, q.Inert, q.NextBoneLength, q.KeepLengthLimitMin, q.KeepLengthLimitMax };
                for (int k = 0; k < 8; k++) e = Mathf.Max(e, Mathf.Abs(got[k] - want[i, k]));
            }
            bool hdr = mL.Root && mL.Root.name == "cf_J_Mune00" && mL.HeavyLoopMaxCount == 3 && Mathf.Abs(mL.UpdateRate - 60) < 1e-6f && mL.PtnNo == 0
                       && (p0.Gravity - new Vector3(0, -0.005f, 0)).magnitude < 1e-7f && (p0.EndOffset - new Vector3(0, 0, 0.1f)).magnitude < 1e-7f
                       && Mathf.Abs(p0.EndOffsetInert - 1f) < 1e-6f && mL.Patterns.Count == 3;
            Check("Mune_L header + pattern 0", hdr && e < 1e-5f, $"root {mL.Root?.name}, cap {mL.HeavyLoopMaxCount}, rate {mL.UpdateRate}, patterns {mL.Patterns.Count} ({string.Join("/", mL.Patterns.Select(p => p.Name))}), gravity {F(p0.Gravity)}, end {F(p0.EndOffset)}, max |param - spec| {e:E1}");
            var p1 = mL.Patterns[1];
            Check("Mune_L pattern 1 (Under)", (p1.Gravity - new Vector3(0, 0.002f, -0.02f)).magnitude < 1e-6f && Mathf.Abs(p1.Params[2].NextBoneLength - 4) < 1e-6f,
                $"gravity {F(p1.Gravity)}, Mune01 NBL {F(p1.Params[2].NextBoneLength)}");
        }
        if (sL)
        {
            var p0 = sL.Patterns[0];
            bool ok = sL.Root && sL.Root.name == "cf_J_SiriDam_R" && sL.HeavyLoopMaxCount == 2 && (p0.EndOffset - new Vector3(0, 0, -0.1f)).magnitude < 1e-7f
                      && Mathf.Abs(p0.Params[1].NextBoneLength - 3) < 1e-6f && Mathf.Abs(p0.EndOffsetDamping - 0.01f) < 1e-6f;
            Check("Siri_L header (Root quirk cf_J_SiriDam_R)", ok, $"root {sL.Root?.name}, cap {sL.HeavyLoopMaxCount}, end {F(p0.EndOffset)}, SiriDam01 NBL {F(p0.Params[1].NextBoneLength)}");
        }

        // Bind-pose rest lengths, LocalPosition, tip.
        var stage = new BustStage(anim, new BustOptions(), false, prefab);
        var bl = stage.Chains[0]; var br = stage.Chains[1]; var hl = stage.Chains[2];
        float[] wantLen = { 0.412f, 0.65f, 0.30f, 0.30f };
        Vector3[] wantLp = { new Vector3(-0.412f, 0, 0), new Vector3(0, 0, 0.65f), new Vector3(0, 0, 0.30f), new Vector3(0, 0, 0.30f) };
        float le = 0, lpe = 0;
        var sbLen = new StringBuilder();
        foreach (var (ch, sign) in new[] { (bl, 1f), (br, -1f) })
        {
            if (ch == null) continue;
            for (int i = 1; i <= 4; i++)
            {
                float len = (ch.ParticleTransform(i - 1).position - ch.ParticleTransform(i).position).magnitude;
                le = Mathf.Max(le, Mathf.Abs(len - wantLen[i - 1]));
                var w = wantLp[i - 1]; w.x *= sign;
                lpe = Mathf.Max(lpe, (ch.LocalPosition(i) - w).magnitude);
                if (sign > 0) sbLen.Append($"{ch.ParticleTransform(i).name} {F(len)} LP {F(ch.LocalPosition(i))}; ");
            }
        }
        Check("bust rest lengths 0.412/0.65/0.30/0.30", bl != null && br != null && le < 1e-3f, $"max error {le:E2}; {sbLen}");
        Check("bust LocalPosition", lpe < 1e-3f, $"max error {lpe:E2} (L and R mirrored)");
        if (bl != null)
        {
            var tip = bl.Position(bl.ParticleCount - 1);
            Check("bust tip world position", (tip - new Vector3(-0.8448f, 13.6594f, 1.1571f)).magnitude < 1e-3f, $"{F(tip)} (want (-0.8448, 13.6594, 1.1571)), particles {bl.ParticleCount}");
        }
        if (hl != null)
        {
            float len = (hl.ParticleTransform(0).position - hl.ParticleTransform(1).position).magnitude;
            Check("hip rest length 0.40", Mathf.Abs(len - 0.4f) < 1e-3f && (hl.LocalPosition(1) - new Vector3(0, 0.0001f, -0.4f)).magnitude < 1e-3f, $"{F(len)}, LocalPosition {F(hl.LocalPosition(1))}, particles {hl.ParticleCount}");
        }

        // Step counts under forced dt.
        var sbs = new StringBuilder();
        bool stepsOk = true;
        foreach (var (dt, wantM, wantS) in new[] { (1f / 144, 60f / 144, 60f / 144), (1f / 60, 1f, 1f), (1f / 30, 2f, 2f), (1f / 20, 3f, 2f), (1f / 10, 3f, 2f) })
        {
            var m = new BustChain(mL, null); var s = new BustChain(sL, null);
            int nm = 0, ns = 0, frames = 288, rm = 0, rs = 0;
            float accM = 0f, accS = 0f;
            // Literal float re-implementation (the game's accumulator has the same float rounding).
            int RefSteps(ref float acc, float rate, int cap)
            {
                float h = 1f / rate; int k = 0; acc += dt;
                while (acc >= h) { acc -= h; k++; if (k >= cap) { acc = 0f; break; } }
                return k;
            }
            for (int f = 0; f < frames; f++)
            {
                nm += m.Accumulate(dt); ns += s.Accumulate(dt);
                rm += RefSteps(ref accM, mL.UpdateRate, mL.HeavyLoopMaxCount); rs += RefSteps(ref accS, sL.UpdateRate, sL.HeavyLoopMaxCount);
            }
            float am = (float)nm / frames, aS = (float)ns / frames;
            sbs.Append($"{1 / dt:0} fps: Mune {am:0.###} (ideal {wantM:0.##}) Siri {aS:0.###} (ideal {wantS:0.##}); ");
            // Equal to the literal accumulator, and within one step of the ideal (float accumulation at 20 fps alternates 2 / 3).
            if (nm != rm || ns != rs || Mathf.Abs(am - wantM) > 0.5f || Mathf.Abs(aS - wantS) > 0.5f) stepsOk = false;
        }
        stage.Chains[0]?.InitTransforms();
        Check("Ver02 steps per frame", stepsOk, sbs + "('20 fps -> 3' holds in exact arithmetic; 0.05 - 2/60 in float is just below 1/60)");

        // Derived bust parameters.
        var rows = new[] { (0.5f, 0.5f, 0.5f, 0.532f, 0.174f, 0.148f, -0.003125f), (1f, 1f, 1f, 0.01f, 0.05f, 0.1f, -0.025f), (0f, 0.7f, 0.3f, 0.982f, 0.199f, 0.198f, 0f), (1f, 0.5f, 0f, 0.0982f, 0.148f, 0.1f, 0f) };
        float pe = 0;
        foreach (var (size, soft, weight, ws, we, wd, wg) in rows)
        {
            stage.SetCardValues(soft, weight, size);
            foreach (var ch in new[] { bl, br })
                for (int i = 0; i < ch.ParticleCount; i++)
                {
                    pe = Mathf.Max(pe, Mathf.Abs(ch.Stiffness(i) - ws)); pe = Mathf.Max(pe, Mathf.Abs(ch.Elasticity(i) - we));
                    pe = Mathf.Max(pe, Mathf.Abs(ch.Damping(i) - wd)); pe = Mathf.Max(pe, Mathf.Abs(ch.Gravity.y - wg));
                }
        }
        bool hipsKept = hl != null && Mathf.Abs(hl.Damping(1) - 0.01f) < 1e-6f && hl.Gravity == Vector3.zero;
        Check("derived bust parameters", pe < 1.5e-3f && hipsKept, $"max error {pe:E2} over 4 rows x all particles of both breasts; hips untouched: {hipsKept}");
    }

    // ------------------------------------------------------------------ 1c
    static void PartChecks(ContentCatalog gd)
    {
        var opt = new DynamicBoneOptions();
        var table = new ColliderTable();
        var none = new List<MonoBehaviour>();
        // Back hair ids 0..2.
        var expect = new Dictionary<int, (int comps, Dictionary<string, (int p, int t, float len)> rows)>
        {
            [0] = (10, new Dictionary<string, (int, int, float)>
            {
                ["c_J_hair_BCa_01"] = (4, 1, 3.7292f), ["c_J_hair_BCb_01"] = (4, 1, 3.7500f), ["c_J_hair_BCRa_01"] = (4, 1, 3.6706f), ["c_J_hair_BCLa_01"] = (4, 1, 3.6706f),
                ["c_J_hair_BCRb_01"] = (4, 1, 3.6975f), ["c_J_hair_BCLb_01"] = (4, 1, 3.6979f), ["c_J_hair_BRa_01"] = (3, 1, 2.7355f), ["c_J_hair_BRb_01"] = (3, 1, 2.7355f),
                ["c_J_hair_BLa_01"] = (3, 1, 2.7355f), ["c_J_hair_BLb_01"] = (3, 1, 2.7355f),
            }),
            [1] = (6, new Dictionary<string, (int, int, float)>
            {
                ["c_J_hair_BCa_01"] = (2, 1, 0.7782f), ["c_J_hair_BCb_01"] = (2, 1, 0.8062f), ["c_J_hair_BLa_01"] = (2, 1, 0.8062f),
                ["c_J_hair_BRa_01"] = (2, 1, 0.8602f), ["c_J_hair_BRb_01"] = (2, 1, 0.8602f), ["c_J_hair_BLb_01"] = (2, 1, 0.8602f),
            }),
            [2] = (1, new Dictionary<string, (int, int, float)> { ["c_J_hair_BCa_01"] = (2, 1, 1.2f) }),
        };
        foreach (var kv in expect)
        {
            var e = gd.GetEntry(300, ContentId.Vanilla(kv.Key));
            var prefab = e != null ? gd.LoadAsset<GameObject>(e, "MainAB", "MainData") : null;
            if (!prefab) { Check($"back hair id {kv.Key}", false, "prefab not loaded"); continue; }
            var go = Keep(Object.Instantiate(prefab));
            go.transform.position = Vector3.zero;
            var dbs = go.GetComponentsInChildren<DynamicBone>(true);
            float le = 0; bool counts = true;
            var sb = new StringBuilder();
            foreach (var db in dbs)
            {
                var ch = new DynamicBoneChain(db, opt, table, none);
                string rn = db.m_Root ? db.m_Root.name : "-";
                sb.Append($"{rn} {ch.ParticleCount}({ch.TipCount}) {F(ch.TotalLength)}; ");
                if (!kv.Value.rows.TryGetValue(rn, out var w)) { counts = false; continue; }
                if (ch.ParticleCount != w.p || ch.TipCount != w.t) counts = false;
                le = Mathf.Max(le, Mathf.Abs(ch.TotalLength - w.len));
            }
            Check($"back hair id {kv.Key} {prefab.name}", dbs.Length == kv.Value.comps && counts && le < 2e-4f, $"{dbs.Length} components (want {kv.Value.comps}), max length error {le:E1}; {sb}");
        }

        // Skirt pleat1 on the body skeleton (re-pointed by name), both chain variants.
        var skirt = gd.LoadAsset<GameObject>("chara/00/fo_bot_00.unity3d", "p_cf_bot_skirt_pleat1");
        if (skirt)
        {
            var anim = FreshAnim(gd, out _);
            var body = Bones(CharaAssembler.Find(anim.transform, "cf_J_Root"));
            var go = Keep(Object.Instantiate(skirt));
            var dbs = go.GetComponentsInChildren<DynamicBone>(true);
            foreach (var db in dbs)
            {
                db.m_Root = body[db.m_Root.name];
                db.m_Exclusions = db.m_Exclusions.Select(t => t && body.TryGetValue(t.name, out var b) ? b : t).ToList();
                if (db.m_notRolls != null) db.m_notRolls = db.m_notRolls.Select(t => t && body.TryGetValue(t.name, out var b) ? b : t).ToList();
            }
            foreach (var variant in new[] { ChainVariant.IllusionFixes, ChainVariant.Vanilla })
            {
                var o = new DynamicBoneOptions { Variant = variant };
                var chains = dbs.Select(db => new DynamicBoneChain(db, o, table, none)).ToList();
                bool ok = dbs.Length == 2 && chains.All(c => c.ParticleCount == 7 && c.TipCount == 0 && Mathf.Abs(c.TotalLength - 2.7498f) < 2e-4f);
                Check($"skirt pleat1 ({variant})", ok, string.Join("; ", chains.Select(c => $"{c.RootName} {c.ParticleCount}({c.TipCount}) {F(c.TotalLength)} excl {c.Source.m_Exclusions.Count}")));
            }
        }
        else Check("skirt pleat1", false, "prefab not loaded");

        var ribbon = gd.LoadAsset<GameObject>("chara/00/ao_head_00.unity3d", "p_acs_ribon00");
        if (ribbon)
        {
            var go = Keep(Object.Instantiate(ribbon));
            go.transform.position = Vector3.zero;
            var chains = go.GetComponentsInChildren<DynamicBone>(true).Select(db => new DynamicBoneChain(db, opt, table, none)).ToList();
            bool ok = chains.Count == 2 && chains.All(c => c.ParticleCount == 2 && Mathf.Abs(c.TotalLength - 0.8203f) < 2e-4f);
            Check("accessory p_acs_ribon00", ok, string.Join("; ", chains.Select(c => $"{c.RootName} {c.ParticleCount}({c.TipCount}) {F(c.TotalLength)} D {F(c.Damping(1))}")));
        }
        else Check("accessory p_acs_ribon00", false, "prefab not loaded");
    }

    // ------------------------------------------------------------------ 2
    // Test rig: owner (DynamicBone) -> b0 (m_Root) -> b1, tip from m_EndOffset; one capsule collider near the chain.
    static (GameObject owner, DynamicBone db, Transform b0, Transform b1, DynamicBoneCollider col) Rig(string name)
    {
        var owner = Keep(new GameObject(name));
        owner.transform.position = new Vector3(0, 10, 0);
        var b0 = new GameObject("b0").transform; b0.SetParent(owner.transform, false); b0.localRotation = Quaternion.Euler(0, 0, 10);
        var b1 = new GameObject("b1").transform; b1.SetParent(b0, false); b1.localPosition = new Vector3(0, -1, 0);
        var colGo = Keep(new GameObject(name + "_col"));
        colGo.transform.position = new Vector3(0.35f, 8.9f, 0f);
        var col = colGo.AddComponent<DynamicBoneCollider>();
        col.m_Direction = 2; col.m_Center = Vector3.zero; col.m_Radius = 0.3f; col.m_Height = 1.5f; col.m_Bound = 0;
        var db = owner.AddComponent<DynamicBone>();
        db.m_Root = b0; db.m_UpdateRate = 60; db.m_UpdateMode = 0;
        db.m_Damping = 0.2f; db.m_Elasticity = 0.1f; db.m_Stiffness = 0.7f; db.m_Inert = 0.5f; db.m_Radius = 0.2f;
        db.m_EndLength = 0; db.m_EndOffset = new Vector3(0, -0.8f, 0.2f);
        db.m_Gravity = new Vector3(0, -0.03f, 0); db.m_Force = new Vector3(0.01f, 0, 0);
        db.m_Colliders = new List<MonoBehaviour> { col };
        db.m_Exclusions = new List<Transform>(); db.m_notRolls = null;
        db.m_DistanceToObject = 20;
        return (owner.gameObject, db, b0, b1, col);
    }

    static void Drive(GameObject owner, int frame)
    {
        owner.transform.position = new Vector3(0.05f * frame, 10f + 0.03f * frame, 0.02f * frame);
        owner.transform.rotation = Quaternion.Euler(12f * frame, 0f, 7f * frame);
    }

    static void ReferenceCheck()
    {
        var main = Rig("dbt_rig_main");
        var refr = Rig("dbt_rig_ref");
        var stage = new DynamicBoneStage(new DynamicBoneOptions { FrameLocked = true });
        var chain = stage.Add(main.db, DynamicBoneCategory.Other);
        stage.ApplySwitches();   // activate at the build pose (both rigs start from the same state)

        // --- independent implementation (3 particles) on the second rig
        var db = refr.db;
        Transform t0 = refr.b0, t1 = refr.b1;
        Vector3 lp0 = t0.localPosition, lp1 = t1.localPosition;
        Quaternion lr0 = t0.localRotation, lr1 = t1.localRotation;
        Vector3 eo = t1.InverseTransformPoint(t1.position + db.transform.rotation * db.m_EndOffset);
        var X = new Vector3[3]; var P = new Vector3[3];
        X[0] = P[0] = t0.position; X[1] = P[1] = t1.position; X[2] = P[2] = t1.TransformPoint(eo);
        Vector3 localG = t0.InverseTransformDirection(db.m_Gravity);
        Vector3 prevPos = db.transform.position;
        float dmp = db.m_Damping, el = db.m_Elasticity, st = db.m_Stiffness, ine = db.m_Inert, rad = db.m_Radius;
        bool firstFrame = true;

        void RefStep()
        {
            if (!firstFrame) { t0.localPosition = lp0; t0.localRotation = lr0; t1.localPosition = lp1; t1.localRotation = lr1; }
            firstFrame = false;
            float scale = Mathf.Abs(db.transform.lossyScale.x);
            Vector3 move = db.transform.position - prevPos; prevPos = db.transform.position;
            Vector3 P0 = t0.position, P1 = t1.position, L1 = t1.localPosition;
            Matrix4x4 M0 = t0.localToWorldMatrix, M1 = t1.localToWorldMatrix;
            Vector3 G = db.m_Gravity, gn = G.normalized, gr = t0.rotation * localG;
            Vector3 Fv = (G - gn * Mathf.Max(0, Vector3.Dot(gr, gn)) + db.m_Force) * scale;
            P[0] = X[0]; X[0] = P0;
            for (int i = 1; i < 3; i++) { Vector3 v = X[i] - P[i]; Vector3 c = move * ine; P[i] = X[i] + c; X[i] = X[i] + v * (1 - dmp) + Fv + c; }
            // collider capsule ends
            var cm = refr.col.transform.localToWorldMatrix;
            float h = refr.col.m_Height / 2 - refr.col.m_Radius;
            Vector3 ca = cm.MultiplyPoint(refr.col.m_Center - new Vector3(0, 0, h)), cb = cm.MultiplyPoint(refr.col.m_Center + new Vector3(0, 0, h));
            float cr = refr.col.m_Radius * Mathf.Abs(refr.col.transform.lossyScale.x) + rad * scale;
            for (int i = 1; i < 3; i++)
            {
                Vector3 q = i == 1 ? L1 : eo;
                Matrix4x4 Mj = i == 1 ? M0 : M1;
                float len = i == 1 ? (P0 - P1).magnitude : (Mj.MultiplyVector(eo)).magnitude;
                Vector3 r = new Vector3(Mj.m00 * q.x + Mj.m01 * q.y + Mj.m02 * q.z, Mj.m10 * q.x + Mj.m11 * q.y + Mj.m12 * q.z, Mj.m20 * q.x + Mj.m21 * q.y + Mj.m22 * q.z) + X[i - 1];
                Vector3 x = X[i];
                x = x + (r - x) * el;
                Vector3 d = r - x; float maxDev = 2 * len * (1 - st);
                if (d.magnitude > maxDev) x = r - d.normalized * maxDev;
                // capsule (outside)
                Vector3 u = cb - ca; float tt = Vector3.Dot(x - ca, u);
                Vector3 c0 = tt <= 0 ? ca : tt >= u.sqrMagnitude ? cb : ca + u * (tt / u.sqrMagnitude);
                Vector3 nn = x - c0;
                if (nn.sqrMagnitude > 0 && nn.sqrMagnitude < cr * cr) x = c0 + nn.normalized * cr;
                Vector3 e = X[i - 1] - x;
                if (e.magnitude > 0) x = X[i - 1] - e.normalized * len;
                X[i] = x;
            }
            // write-back
            t0.rotation = Quaternion.FromToRotation(t0.rotation * L1, X[1] - X[0]) * t0.rotation;
            t1.position = X[1];
            t1.rotation = Quaternion.FromToRotation(t1.TransformDirection(eo), X[2] - X[1]) * t1.rotation;
        }

        var frame = new MotionFrame(1f / 60, 1f / 60, 0, 0);
        double worst = 0;
        var sb = new StringBuilder();
        for (int f = 1; f <= 10; f++)
        {
            Drive(main.owner, f); Drive(refr.owner, f);
            stage.Step(frame);
            RefStep();
            double e = 0;
            for (int i = 0; i < 3; i++) e = Math.Max(e, (chain.Position(i) - X[i]).magnitude);
            e = Math.Max(e, (main.b1.position - refr.b1.position).magnitude);
            e = Math.Max(e, RotationCompare.Deg(main.b1.rotation, refr.b1.rotation) * Math.PI / 180);
            e = Math.Max(e, RotationCompare.Deg(main.b0.rotation, refr.b0.rotation) * Math.PI / 180);
            if (f == 1 || f == 2 || f == 10) { sb.Append($"step {f}: {e:E2} (tip {F(X[2])}); "); worst = Math.Max(worst, e); }
        }
        float pen = DynamicBoneColliders.Penetration(stage.Colliders[chain.ColliderIndex(0)], chain.Position(1), main.db.m_Radius);
        Check("3-particle chain vs reference", worst < 1e-5f, $"max |dx| / bone angle (rad) {worst:E2}; {sb}collider touched: {pen < 1e-4f}");

        // gravity cancellation. Root at rest -> no gravity displacement; root rotated 90° about X -> full gravity.
        var g = Rig("dbt_rig_g");
        g.db.m_Colliders.Clear(); g.db.m_Force = Vector3.zero; g.db.m_Damping = 0; g.db.m_Elasticity = 0; g.db.m_Stiffness = 0; g.db.m_Inert = 0;
        g.db.m_Gravity = new Vector3(0, -0.05f, 0);
        g.b0.localRotation = Quaternion.identity;
        var gs = new DynamicBoneStage(new DynamicBoneOptions { FrameLocked = true });
        var gc = gs.Add(g.db, DynamicBoneCategory.Other);
        gs.Step(frame);
        float rest = (gc.Position(2) - gc.PrevPosition(2)).magnitude;
        // rotated: the vertical bone becomes horizontal; the first step moves the tip by the full gravity before the
        // length constraint, which then keeps it on the sphere; measure the vertical drop of the mid particle.
        g.owner.transform.rotation = Quaternion.Euler(90, 0, 0);
        gs.Reset();
        Vector3 before = gc.Position(1);
        gs.Step(frame);
        float drop = before.y - gc.Position(1).y;
        Check("gravity rest cancellation", rest < 1e-6f && drop > 0.0005f, $"unrotated displacement {rest:E2}; rotated 90° X: mid particle drop {F(drop)} (gravity per step 0.05 before the length constraint)");
    }

    // ------------------------------------------------------------------ 3 TimeScale
    static void TimeScaleCheck()
    {
        float Run(float timeScale, int frames, out int steps, out Vector3 tip)
        {
            var rig = Rig("dbt_rig_ts" + timeScale);
            rig.db.m_Colliders.Clear();
            var m = rig.owner.AddComponent<CharaMotion>();
            m.AutoStep = false; m.TimeScale = timeScale;
            var stage = new DynamicBoneStage(new DynamicBoneOptions());
            var c = stage.Add(rig.db, DynamicBoneCategory.Other);
            m.Add(stage);
            m.Step(1f / 60);   // activate at rest
            rig.owner.transform.position += new Vector3(1f, 0, 0);   // impulse (inert 0.5 -> swing)
            steps = 0;
            for (int f = 0; f < frames; f++) { m.Step(1f / 60); steps += c.StepsLastFrame; }
            tip = c.Position(2);
            return 0f;
        }
        Run(1f, 20, out int s1, out var tip1);
        Run(0.5f, 40, out int s2, out var tip2);
        float d = (tip1 - tip2).magnitude;
        Check("TimeScale 0.5 halves the motion per frame", s1 == 20 && s2 == 20 && d < 1e-2f, $"20 frames at 1.0: {s1} steps; 40 frames at 0.5: {s2} steps; tip difference {d:E2} units after a 1-unit impulse (the in-between frames run the skip update: stiffness cone + length)");
    }

    // ------------------------------------------------------------------ 3 / 4 card
    struct Snap { public Vector3[] X; }

    static IEnumerable<(DynamicBoneChain c, int i)> Particles(DynamicBoneStage s)
    {
        foreach (var c in s.Chains) if (c.Active) for (int i = 1; i < c.ParticleCount; i++) yield return (c, i);
    }

    static float MaxSpeed(DynamicBoneStage s, BustStage b, out string where)
    {
        float m = 0; where = "-";
        foreach (var (c, i) in Particles(s))
        {
            float v = (c.Position(i) - c.PrevPosition(i)).magnitude;
            if (v > m) { m = v; where = $"{c.Category} {c.RootName}#{i}"; }
        }
        if (b != null)
            foreach (var c in b.All)
            {
                if (!c.Active) continue;
                for (int i = 1; i < c.ParticleCount; i++)
                {
                    float v = (c.Position(i) - c.PrevPosition(i)).magnitude;
                    if (v > m) { m = v; where = $"{c.Comment}#{i}"; }
                }
            }
        return m;
    }

    static bool Finite(DynamicBoneStage s, BustStage b)
    {
        foreach (var c in s.Chains) for (int i = 0; i < c.ParticleCount; i++) if (!FiniteV(c.Position(i))) return false;
        if (b != null) foreach (var c in b.All) for (int i = 0; i < c.ParticleCount; i++) if (!FiniteV(c.Position(i))) return false;
        return true;
    }

    static bool FiniteV(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

    static List<Vector3> All(DynamicBoneStage s, BustStage b)
    {
        var l = new List<Vector3>();
        foreach (var c in s.Chains) for (int i = 0; i < c.ParticleCount; i++) { l.Add(c.Position(i)); var t = c.ParticleTransform(i); if (t) l.Add(t.position); }
        if (b != null) foreach (var c in b.All) for (int i = 0; i < c.ParticleCount; i++) { l.Add(c.Position(i)); var t = c.ParticleTransform(i); if (t) l.Add(t.position); }
        return l;
    }

    static float MaxDiff(List<Vector3> a, List<Vector3> b)
    {
        float m = 0; for (int i = 0; i < Math.Min(a.Count, b.Count); i++) m = Mathf.Max(m, (a[i] - b[i]).magnitude);
        return a.Count == b.Count ? m : float.PositiveInfinity;
    }

    // Distance of each particle to its parent particle relative to the rest length measured at build.
    static Dictionary<DynamicBoneChain, float[]> RestLengths(DynamicBoneStage s)
    {
        var d = new Dictionary<DynamicBoneChain, float[]>();
        foreach (var c in s.Chains)
        {
            var l = new float[c.ParticleCount];
            for (int i = 1; i < c.ParticleCount; i++) l[i] = (c.Position(i) - c.Position(c.Parent(i))).magnitude;
            d[c] = l;
        }
        return d;
    }

    static float LengthError(DynamicBoneStage s, Dictionary<DynamicBoneChain, float[]> rest, out string where)
    {
        float m = 0; where = "-";
        foreach (var (c, i) in Particles(s))
        {
            if (!rest.TryGetValue(c, out var l) || l[i] <= 1e-6f) continue;
            float e = Mathf.Abs((c.Position(i) - c.Position(c.Parent(i))).magnitude - l[i]) / l[i];
            if (e > m) { m = e; where = $"{c.RootName}#{i}"; }
        }
        return m;
    }

    static bool CardDynamics(ContentCatalog gd, string cardPath, int frames)
    {
        if (!File.Exists(cardPath)) { Check("card", false, "not found: " + cardPath); return false; }
        var card = Card.Load(cardPath);
        var asm = new CharaAssembler(gd);
        var root = asm.BuildFromDocuments(card);
        trash.Add(root);
        var ctx = asm.Context;
        bool male = asm.IsMale;
        foreach (var l in asm.Log.Where(l => l.StartsWith("dynbone") || l.StartsWith("male") || l.StartsWith("motion") || l.StartsWith("hair") || l.StartsWith("ct_") || l.StartsWith("acc")))
            Info("log: " + l);
        var motion = ctx.Motion;
        var dyn = motion.Get<DynamicBoneStage>();
        var bust = motion.Get<BustStage>();
        Check("stages attached", dyn != null && bust != null && motion.Errors == 0, $"stages: {string.Join(", ", motion.Stages.Select(s => s.Name + "@" + (int)s.Phase))}; motion errors {motion.Errors}");
        if (dyn == null || bust == null) return male;
        Check("Ver02 chains", bust.All.Count == 4 && bust.Chains.All(c => c != null && c.ParticleCount >= 3), string.Join("; ", bust.All.Select(c => c.ToString())));
        foreach (var c in dyn.Chains) Info("chain " + c);
        Info($"particles: dynbone {dyn.ParticleCount} in {dyn.Chains.Count} chains, bust {bust.All.Sum(c => c.ParticleCount)}; collider table {dyn.Colliders.Count}");

        // Male: chains on, dbcBust colliders off and seen as zero records.
        if (male)
        {
            var cbb = ctx.Anim.GetComponentInChildren<AIChara.CmpBoneBody>(true);
            int off = cbb?.dbcBust?.Count(c => c && !c.enabled) ?? 0;
            dyn.Colliders.Update();
            int zeroRec = 0;
            for (int i = 0; i < dyn.Colliders.Count; i++)
            {
                var src = dyn.Colliders.Source(i) as DynamicBoneCollider;
                if (src && !src.enabled && dyn.Colliders[i].Radius == 0 && dyn.Colliders[i].A == Vector3.zero) zeroRec++;
            }
            motion.Step(1f / 60);
            bool on = bust.All.All(c => c.Active);
            Check("male: bust chains on, bust colliders off", on && off == 4 && bust.All.All(c => c.Source.enabled), $"Ver02 active {bust.All.Count(c => c.Active)}/4, dbcBust disabled {off}/4, zero records in the collider table {zeroRec}; size {F(bust.BustSize)} softness {F(bust.BustSoftness)} weight {F(bust.BustWeight)}");
        }

        // ---- run N frames
        motion.ResetAll();
        var rest = RestLengths(dyn);
        // Ver02 rest lengths at the reference pose (ResetAll ran InitTransforms): bone distance, tip |EndOffset| · Root scale.
        var bustRest = new Dictionary<BustChain, float[]>();
        foreach (var c in bust.All)
        {
            var l = new float[c.ParticleCount];
            for (int i = 1; i < c.ParticleCount; i++)
            {
                var t = c.ParticleTransform(i); var tp = c.ParticleTransform(i - 1);
                l[i] = t ? (tp.position - t.position).magnitude : c.EndOffset.magnitude * Mathf.Abs(c.Root.lossyScale.x);
            }
            bustRest[c] = l;
        }
        bool finite = true;
        int settle = 0;
        float lastSpeed = 0; string lastWhere = "-";
        float[] speeds = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            motion.Step(1f / 60);
            if (!Finite(dyn, bust)) { finite = false; break; }
            speeds[f] = MaxSpeed(dyn, bust, out lastWhere);
            lastSpeed = speeds[f];
        }
        for (int f = frames - 1; f >= 0; f--) if (speeds[f] >= 1e-3f) { settle = f + 1; break; }
        Check("finite (no NaN / Inf)", finite && motion.Errors == 0, $"{frames} frames, motion errors {motion.Errors}");
        Check("chains settle", lastSpeed < 1e-3f, $"max particle speed at the end {lastSpeed:E2} units/frame ({lastWhere}); last frame with speed >= 1e-3: {settle} of {frames}; speeds at 1/10/60/120: {Sp(speeds, 0)} {Sp(speeds, 9)} {Sp(speeds, 59)} {Sp(speeds, 119)}");
        float lenErr = LengthError(dyn, rest, out string lw);
        Check("bone lengths kept (DynamicBone)", lenErr < 1e-3f, $"max relative length error {lenErr:E2} ({lw})");
        float bustHard = 0f, winLo = 1f, winHi = 1f;
        bool inWindow = true;
        foreach (var c in bust.All)
        {
            if (!c.Active) continue;
            var l = bustRest[c];
            for (int i = 1; i < c.ParticleCount; i++)
            {
                if (l[i] <= 1e-6f) continue;
                float ratio = (c.Position(i) - c.Position(i - 1)).magnitude / l[i];
                if (c.KeepMin(i) == 0f && c.KeepMax(i) == 0f) { bustHard = Mathf.Max(bustHard, Mathf.Abs(ratio - 1f)); continue; }
                // k = 1 - l/d within [KeepMin, KeepMax]  <=>  d/l within [1/(1-KeepMin), 1/(1-KeepMax)]
                winLo = Mathf.Min(winLo, ratio); winHi = Mathf.Max(winHi, ratio);
                if (ratio < 1f / (1f - c.KeepMin(i)) - 1e-3f || ratio > 1f / (1f - c.KeepMax(i)) + 1e-3f) inWindow = false;
            }
        }
        Check("bust lengths (hard / keep window)", bustHard < 1e-3f && inWindow, $"hard particles: max |d/l - 1| {bustHard:E2}; window particles (Mune03, -0.1..0.1): d/l in [{F(winLo)}, {F(winHi)}] (allowed [0.909, 1.111])");

        // ---- collider penetration
        dyn.Colliders.Update();
        float pen = 0, penSum = 0; int penN = 0, tested = 0; string pw = "-";
        foreach (var (c, i) in Particles(dyn))
        {
            for (int k = 0; k < c.ColliderCount; k++)
            {
                var s = dyn.Colliders[c.ColliderIndex(k)];
                if (s.Kind != ColliderShapeKind.Capsule) continue;
                tested++;
                float p = DynamicBoneColliders.Penetration(s, c.Position(i), c.Radius(i) * c.ObjScale);
                if (p > 1e-4f) { penN++; penSum += p; }
                if (p > pen) { pen = p; pw = $"{c.RootName}#{i} in {dyn.Colliders.Source(c.ColliderIndex(k)).name}"; }
            }
        }
        Check("particles outside capsules", pen < 0.1f, $"max penetration {F(pen)} units ({pw}); {penN} of {tested} particle-capsule pairs penetrate by > 1e-4 (mean {(penN > 0 ? penSum / penN : 0):0.####}); the hard length constraint runs after the colliders");

        // ---- Step(0) holds
        var a0 = All(dyn, bust);
        motion.Step(0f);
        float hold = MaxDiff(a0, All(dyn, bust));
        Check("Step(0) holds state", hold < 1e-4f, $"max change {hold:E2} (particles and bones)");

        // ---- 1 m teleport of the root (inert), then ResetAll
        var rt = ctx.Root.transform;
        float maxV = 0, maxLen = 0;
        rt.position += new Vector3(10f, 0, 0);
        bool fin2 = true;
        for (int f = 0; f < 180; f++)
        {
            motion.Step(1f / 60);
            if (!Finite(dyn, bust)) { fin2 = false; break; }
            maxV = Mathf.Max(maxV, MaxSpeed(dyn, bust, out _));
            maxLen = Mathf.Max(maxLen, LengthError(dyn, rest, out _));
            if (f == 2) break;
        }
        float vAfter = MaxSpeed(dyn, bust, out string vw);
        motion.ResetAll();
        float vReset = 0;
        foreach (var c in dyn.Chains) for (int i = 0; i < c.ParticleCount; i++) vReset = Mathf.Max(vReset, (c.Position(i) - c.PrevPosition(i)).magnitude);
        foreach (var c in bust.All) for (int i = 0; i < c.ParticleCount; i++) vReset = Mathf.Max(vReset, (c.Position(i) - c.PrevPosition(i)).magnitude);
        Check("ResetAll drops inertia", vReset == 0f && vAfter > 1e-3f, $"max |x - prev| before reset {vAfter:E2} ({vw}), after {vReset:E2}");
        rt.position -= new Vector3(10f, 0, 0);
        motion.ResetAll();
        for (int f = 0; f < 120; f++) motion.Step(1f / 60);
        rt.position += new Vector3(10f, 0, 0);
        maxV = 0; maxLen = 0;
        for (int f = 0; f < 240; f++)
        {
            motion.Step(1f / 60);
            if (!Finite(dyn, bust)) { fin2 = false; break; }
            maxV = Mathf.Max(maxV, MaxSpeed(dyn, bust, out _));
            maxLen = Mathf.Max(maxLen, LengthError(dyn, rest, out _));
        }
        float vEnd = MaxSpeed(dyn, bust, out string ve);
        Check("1 m teleport does not explode", fin2 && maxV < 10f && maxLen < 1e-3f && vEnd < 5e-3f, $"max particle speed {F(maxV)} units/frame (10 = the jump), max length error {maxLen:E2}, speed after 240 frames {vEnd:E2} ({ve})");

        // ---- head turned 30° instantly: hair lags, then settles
        var hairChains = dyn.Chains.Where(c => c.Active && c.Category == DynamicBoneCategory.Hair && c.ParticleCount > 1).ToList();
        if (hairChains.Count > 0 && ctx.BodyBones.TryGetValue("cf_J_Head", out var headT))
        {
            for (int f = 0; f < 120; f++) motion.Step(1f / 60);
            var before = new List<Vector3>();
            foreach (var c in hairChains) for (int i = 1; i < c.ParticleCount; i++) before.Add(headT.InverseTransformPoint(c.Position(i)));
            headT.localRotation = headT.localRotation * Quaternion.Euler(0, 30, 0);
            motion.Step(1f / 60);
            float lag = 0; int k = 0;
            foreach (var c in hairChains) for (int i = 1; i < c.ParticleCount; i++) lag = Mathf.Max(lag, (headT.InverseTransformPoint(c.Position(i)) - before[k++]).magnitude);
            int settleF = -1;
            for (int f = 0; f < 600; f++)
            {
                motion.Step(1f / 60);
                if (settleF < 0 && MaxSpeed(dyn, null, out _) < 1e-3f) settleF = f + 1;
            }
            float back = 0; k = 0;
            foreach (var c in hairChains) for (int i = 1; i < c.ParticleCount; i++) back = Mathf.Max(back, (headT.InverseTransformPoint(c.Position(i)) - before[k++]).magnitude);
            float vH = MaxSpeed(dyn, null, out string hw);
            Check("head 30°: hair lags then settles", lag > 0.02f && vH < 1e-3f && settleF > 0, $"{hairChains.Count} hair chains; max head-local lag after 1 frame {F(lag)} units; settled (speed < 1e-3) after {settleF} frames; end speed {vH:E2} ({hw}); head-local offset vs before the turn {F(back)} (world-space force / gravity terms)");
            headT.localRotation = headT.localRotation * Quaternion.Euler(0, -30, 0);
        }
        else Info("no active hair chain (head turn check skipped)");

        // ---- bust sweep
        if (!male)
        {
            float s0 = bust.BustSoftness, w0 = bust.BustWeight;
            float e = 0; var sb = new StringBuilder();
            foreach (float soft in new[] { 0f, 0.5f, 1f })
                foreach (float w in new[] { 0f, 0.5f, 1f })
                {
                    bust.SetCardValues(soft, w, bust.BustSize);
                    BustStage.BustSoftParams(soft, bust.BustSize, out float d, out float el, out float st);
                    float gy = BustStage.BustGravityY(soft, w, bust.BustSize);
                    for (int kk = 0; kk < 2; kk++)
                    {
                        var c = bust.Chains[kk];
                        for (int i = 0; i < c.ParticleCount; i++) e = Mathf.Max(e, Mathf.Max(Mathf.Abs(c.Damping(i) - d), Mathf.Max(Mathf.Abs(c.Elasticity(i) - el), Mathf.Abs(c.Stiffness(i) - st))));
                        e = Mathf.Max(e, Mathf.Abs(c.Gravity.y - gy));
                    }
                    if (w == 0.5f) sb.Append($"soft {soft}: S {F(st)} E {F(el)} D {F(d)}; ");
                    if (soft == 1f) sb.Append($"weight {w}: g {F(gy)}; ");
                }
            // Softer settings must hang lower: settle with soft 0 / weight 0 vs soft 1 / weight 1.
            float Sag(float soft, float w)
            {
                bust.SetCardValues(soft, w, bust.BustSize);
                motion.ResetAll();
                for (int f = 0; f < 240; f++) motion.Step(1f / 60);
                var c = bust.Chains[0];
                return c.Position(c.ParticleCount - 1).y;
            }
            float hard = Sag(0, 0), soft1 = Sag(1, 1);
            bust.SetCardValues(s0, w0, bust.BustSize);
            Check("bust softness / weight sweep", e < 1e-6f && soft1 < hard, $"max |param - formula| {e:E1} over 9 settings (BustSize {F(bust.BustSize)}); tip height soft0/weight0 {F(hard)} vs soft1/weight1 {F(soft1)}; {sb}");
        }

        // ---- performance
        var swd = new Stopwatch(); var swb = new Stopwatch();
        var fr = new MotionFrame(1f / 60, 1f / 60, 0, 0);
        for (int f = 0; f < 300; f++)
        {
            swb.Start(); bust.Step(fr); swb.Stop();
            swd.Start(); dyn.Step(fr); swd.Stop();
        }
        double dms = swd.Elapsed.TotalMilliseconds / 300, bms = swb.Elapsed.TotalMilliseconds / 300;
        int parts = dyn.ParticleCount;
        // Stress: eight copies of back hair id 0 (36 particles each) under the head with the 21 body colliders.
        string stress = "";
        double sms = 0;
        var hairE = gd.GetEntry(300, ContentId.Vanilla(0));
        var hairP = hairE != null ? gd.LoadAsset<GameObject>(hairE, "MainAB", "MainData") : null;
        ctx.HeadBones.TryGetValue("N_hair_Root", out var hairParent);
        if (hairP && hairParent && dyn.Chains.Count > 0)
        {
            var big = new DynamicBoneStage(new DynamicBoneOptions());
            var cols = dyn.Chains[0].Source.m_Colliders;
            for (int k = 0; k < 8; k++)
            {
                var go = Object.Instantiate(hairP, hairParent, false);
                foreach (var db in go.GetComponentsInChildren<DynamicBone>(true)) { db.m_Colliders = new List<MonoBehaviour>(cols); big.Add(db, DynamicBoneCategory.Hair, 0); }
            }
            for (int f = 0; f < 30; f++) big.Step(fr);
            var sws = new Stopwatch();
            double Best(DynamicBoneStage st)
            {
                double best = double.MaxValue, sum = 0;
                for (int b = 0; b < 6; b++)
                {
                    sws.Restart();
                    for (int f = 0; f < 50; f++) st.Step(fr);
                    double ms = sws.Elapsed.TotalMilliseconds / 50;
                    best = Math.Min(best, ms); sum += ms;
                }
                lastMean = sum / 6;
                return best;
            }
            sms = Best(big);
            double bigMean = lastMean;
            stress = $"; stress {big.ParticleCount} particles in {big.Chains.Count} chains x {big.Colliders.Count} colliders: {sms:0.000} ms/step (best of 6 x 50 steps; mean {bigMean:0.000})";
            // Same chains without colliders (cost split: transforms + integration vs collider tests).
            var bare = new DynamicBoneStage(new DynamicBoneOptions());
            foreach (var c in big.Chains) bare.Add(c.Source, DynamicBoneCategory.Hair, 0, -1, new List<MonoBehaviour>());
            big.Enabled = false;
            for (int f = 0; f < 30; f++) bare.Step(fr);
            stress += $" (without colliders {Best(bare):0.000} ms)";
        }
        long a0b = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 100; f++) { bust.Step(fr); dyn.Step(fr); }
        long alloc = GC.GetAllocatedBytesForCurrentThread() - a0b;
        Check("no per-step allocations", alloc == 0, $"{alloc} bytes allocated over 100 bust + dynbone steps");
        // Target: ~300 particles well under 0.3 ms per step. The stress set is measured in the editor (Mono JIT, shared machine):
        // reported against the target, checked against 0.4 ms so that machine load does not flip the result.
        var opt = UnityEditor.Compilation.CompilationPipeline.codeOptimization;
        Check("performance (this character)", dms + bms < 0.3, $"dynbone {dms:0.000} ms/step for {parts} particles x {dyn.Colliders.Count} colliders, bust {bms:0.000} ms/step (editor, Mono, {opt})");
        if (sms > 0) Check("performance (stress, target 0.3 ms)", sms < 0.4, $"{stress.TrimStart(';', ' ')}; {(sms < 0.3 ? "within" : "ABOVE")} the 0.3 ms target");
        return male;
    }

    static void FastPathCheck(ContentCatalog gd, string cardPath)
    {
        var builds = new List<(CharaContext ctx, DynamicBoneStage dyn)>();
        bool savedL = DynamicBoneSetup.Options.LocalWriteBack, savedB = DynamicBoneSetup.Options.ColliderBroadPhase;
        foreach (var (local, broad) in new[] { (true, true), (true, false), (false, false) })
        {
            DynamicBoneSetup.Options.LocalWriteBack = local;
            DynamicBoneSetup.Options.ColliderBroadPhase = broad;
            var asm = new CharaAssembler(gd);
            trash.Add(asm.BuildFromDocuments(cardPath));
            builds.Add((asm.Context, asm.Context.Motion.Get<DynamicBoneStage>()));
        }
        DynamicBoneSetup.Options.LocalWriteBack = savedL; DynamicBoneSetup.Options.ColliderBroadPhase = savedB;
        float broadDiff = 0, localDiff = 0;
        for (int f = 0; f < 240; f++)
        {
            foreach (var (ctx, _) in builds)
            {
                if (f == 60) ctx.Root.transform.position += new Vector3(3f, 0, 1f);
                if (f == 120 && ctx.BodyBones.TryGetValue("cf_J_Head", out var h)) h.localRotation *= Quaternion.Euler(10, 30, 0);
                if (f >= 150 && f < 180 && ctx.BodyBones.TryGetValue("cf_J_Spine02", out var sp)) sp.localRotation *= Quaternion.Euler(0, 0, 1.5f);
                ctx.Motion.Step(1f / 60);
            }
            var a0 = All(builds[0].dyn, null);
            broadDiff = Mathf.Max(broadDiff, MaxDiff(a0, All(builds[1].dyn, null)));
            localDiff = Mathf.Max(localDiff, MaxDiff(All(builds[1].dyn, null), All(builds[2].dyn, null)));
        }
        Check("collider broad-phase is exact", broadDiff == 0f, $"max difference broad-phase on vs off {broadDiff:E2} over 240 frames with a move, a head turn and a spine bend ({builds[0].dyn.Chains.Count} chains)");
        Check("local write-back == world write-back", localDiff < 1e-4f, $"max particle / bone position difference {localDiff:E2} (float rounding of the composed matrices)");
    }

    // Builds the first N cards (CardImportTest order), steps each 120 frames: attach errors, motion errors, NaN, settle.
    static void Sweep(ContentCatalog gd, int limit)
    {
        string dir = Path.Combine(CardResolveTest.GameRoot, "UserData", "chara", "female");
        var files = Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories)
            .OrderBy(f => f.Substring(dir.Length).TrimStart('\\', '/').Replace('\\', '/'), StringComparer.Ordinal).Take(limit).ToList();
        int built = 0, attachErr = 0, stepErr = 0, nan = 0, unsettled = 0, chains = 0, particles = 0, maxParticles = 0;
        double maxMs = 0, sumMs = 0;
        foreach (var f in files)
        {
            GameObject root = null;
            try
            {
                var asm = new CharaAssembler(gd);
                root = asm.BuildFromDocuments(f);
                built++;
                if (asm.Log.Any(l => l.StartsWith("motion: dynbone") || l.Contains("dynbone setup failed"))) { attachErr++; Info("attach: " + Path.GetFileName(f) + ": " + asm.Log.First(l => l.Contains("dynbone"))); }
                var m = asm.Context.Motion;
                var dyn = m.Get<DynamicBoneStage>(); var bust = m.Get<BustStage>();
                if (dyn == null || bust == null) { attachErr++; continue; }
                chains += dyn.Chains.Count; particles += dyn.ParticleCount; maxParticles = Math.Max(maxParticles, dyn.ParticleCount);
                var fr = new MotionFrame(1f / 60, 1f / 60, 0, 0);
                var sw = new Stopwatch();
                for (int k = 0; k < 120; k++) { m.Step(1f / 60); if (k >= 60) { sw.Start(); dyn.Step(fr); bust.Step(fr); sw.Stop(); } }
                double ms = sw.Elapsed.TotalMilliseconds / 60;
                sumMs += ms; maxMs = Math.Max(maxMs, ms);
                if (m.Errors > 0) { stepErr++; Info($"motion errors {m.Errors}: {Path.GetFileName(f)}: {string.Join(" | ", asm.Log.Where(l => l.StartsWith("motion:")))}"); }
                if (!Finite(dyn, bust)) { nan++; Info("NaN: " + Path.GetFileName(f)); }
                float v = MaxSpeed(dyn, bust, out string w);
                if (v > 1e-2f) { unsettled++; Info($"moving after 120 frames: {Path.GetFileName(f)} {v:E1} at {w}"); }
            }
            catch (Exception e) { attachErr++; Info($"exception {Path.GetFileName(f)}: {e.GetType().Name}: {e.Message}"); }
            finally { if (root) Object.DestroyImmediate(root); }
        }
        Check("sweep", built == files.Count && attachErr == 0 && stepErr == 0 && nan == 0, $"{built}/{files.Count} built, attach errors {attachErr}, motion errors {stepErr}, NaN {nan}, still moving (> 1e-2/frame) {unsettled}; {chains} chains / {particles} particles in total (max {maxParticles} per character); dynbone + bust step mean {sumMs / Math.Max(1, built):0.000} ms, max {maxMs:0.000} ms per character");
    }

    static double lastMean;

    static string Sp(float[] a, int i) => i < a.Length ? a[i].ToString("E1") : "-";
}
