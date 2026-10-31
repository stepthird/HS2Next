// Batch test of the joint correction (Runtime/Motion/Corrective).
// Run: -executeMethod JointCorrectionTest.Run [-card <path>] [-maleCard <path>]
// Default cards: <game>/UserData/chara/female/BR-Chan HS2 Rev2.png and the first loadable male card of
// <game>/UserData/chara/male (sorted by name).
//  1. table: cf_expression parsed from list/expression.unity3d (26 rows, 18 Euler / 8 LookAt, 8 categories,
//     spot checks, field-by-field equal to the embedded fallback table, cm_expression identical);
//  2. math: Decompose/Compose round trip per order on random rotations, alternate solution;
//  3. binding: on the bare p_cf_anim prefab and on the card builds (driven and source bones, 38 *_dam
//     transforms on p_cf_anim, 0 on p_cf_head_bone);
//  4. worked examples E0-E7 on the bare rest skeleton (all categories on, one evaluation),
//     tolerance 1e-3 per component, sign-insensitive; the same with each fix flag on;
//  5. behaviours on the female build: all off = untouched, per-category isolation, restore on disable,
//     Step(0) evaluates, idempotence, ResetAll, facade (scene 8 / pose 4 flags, Refresh), no allocation;
//     male build binds the same.
// Output: Spike/out/motion/correct_report.txt; EditorApplication.Exit(1) on any FAIL.
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
using HS2Next.Motion.Corrective;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class JointCorrectionTest
{
    const float Tol = 1e-3f;
    static readonly StringBuilder sb = new StringBuilder();
    static int fails, passes;
    static float maxExampleErr;

    static string Arg(string name, string def)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    static void Check(string what, bool ok, string detail = null)
    {
        if (ok) passes++; else fails++;
        sb.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {what}{(detail != null ? "  -- " + detail : "")}");
    }

    static void Info(string line) => sb.AppendLine("        " + line);

    static Quaternion Q(float x, float y, float z, float w) => new Quaternion(x, y, z, w);
    static Quaternion Rx(float a) => Quaternion.AngleAxis(a, Vector3.right);
    static Quaternion Ry(float a) => Quaternion.AngleAxis(a, Vector3.up);
    static Quaternion Rz(float a) => Quaternion.AngleAxis(a, Vector3.forward);
    static string F(Quaternion q) => $"({q.x:F4}, {q.y:F4}, {q.z:F4}, {q.w:F4})";

    // Sign-insensitive max component error.
    static float Err(Quaternion a, Quaternion b)
    {
        float p = Mathf.Max(Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y)), Mathf.Max(Mathf.Abs(a.z - b.z), Mathf.Abs(a.w - b.w)));
        float m = Mathf.Max(Mathf.Max(Mathf.Abs(a.x + b.x), Mathf.Abs(a.y + b.y)), Mathf.Max(Mathf.Abs(a.z + b.z), Mathf.Abs(a.w + b.w)));
        return Mathf.Min(p, m);
    }

    static double AngleDeg(Quaternion a, Quaternion b)
    {
        double d = Math.Abs((double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z + (double)a.w * b.w);
        return 2.0 * Math.Acos(Math.Min(1.0, d)) * 180.0 / Math.PI;
    }

    static Dictionary<string, Transform> Dict(Transform root)
    {
        var d = new Dictionary<string, Transform>();
        void Walk(Transform t)
        {
            if (!d.ContainsKey(t.name)) d[t.name] = t;
            for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i));
        }
        Walk(root);
        return d;
    }

    static int CountDam(Transform root) => root.GetComponentsInChildren<Transform>(true).Count(t => t.name.IndexOf("dam", StringComparison.OrdinalIgnoreCase) >= 0);

    static readonly MotionFrame Frame0 = new MotionFrame(0f, 0f, 0f, 0);

    public static void Run()
    {
        sb.Clear(); fails = 0; passes = 0; maxExampleErr = 0f;
        var created = new List<GameObject>();
        ContentCatalog gd = null;
        string outDir = Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "motion");
        Directory.CreateDirectory(outDir);
        var total = Stopwatch.StartNew();
        try
        {
            var head = new StringBuilder();
            gd = CardResolveTest.BuildDb(head);
            sb.AppendLine("joint correction test");
            sb.Append(head);
            var table = TestTable(gd);
            TestMath();
            if (table != null)
            {
                TestExamples(gd, table, created);
                TestBuilds(gd, created);
            }
        }
        catch (Exception e)
        {
            fails++;
            sb.AppendLine($"  FAIL  exception {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
        }
        foreach (var go in created) if (go) Object.DestroyImmediate(go);
        gd?.UnloadAll();
        sb.Insert(0, $"RESULT: {(fails == 0 ? "PASS" : "FAIL")}  ({passes} passed, {fails} failed; max example error {maxExampleErr:E2}; {total.Elapsed.TotalSeconds:F1} s)\n");
        File.WriteAllText(Path.Combine(outDir, "correct_report.txt"), sb.ToString());
        Debug.Log($"JOINT CORRECTION TEST DONE: {(fails == 0 ? "PASS" : "FAIL")} {passes} passed, {fails} failed, max example error {maxExampleErr:E2}");
        if (fails > 0) TestExit.Exit(1);
    }

    // ---------------------------------------------------------------- 1. table

    static JointCorrectionTable TestTable(ContentCatalog gd)
    {
        sb.AppendLine();
        sb.AppendLine("1. table");
        var ta = gd.LoadAsset<TextAsset>(JointCorrectionTable.Bundle, JointCorrectionTable.FemaleAsset);
        Check("cf_expression found in list/expression.unity3d", ta != null);
        var tm = gd.LoadAsset<TextAsset>(JointCorrectionTable.Bundle, JointCorrectionTable.MaleAsset);
        if (ta != null && tm != null) Check("cm_expression identical to cf_expression", ta.text == tm.text, $"{ta.text.Length} / {tm.text.Length} chars");
        var fallback = JointCorrectionTable.Vanilla();
        Check("the embedded fallback table parses", fallback != null && fallback.Rows.Count == 26 && fallback.Warnings.Count == 0);
        if (ta == null) return fallback;
        var t = JointCorrectionTable.Parse(ta.text, out string err, "cf_expression");
        Check("parse", t != null, err);
        if (t == null) return null;
        var lines = ta.text.Replace("\r", "").Split('\n');
        Info($"text: {ta.text.Length} chars, {lines.Length} lines; line 0 = '{lines[0].Replace("\t", "|")}'");
        Info("row 0 = '" + lines[1].Replace("\t", "|") + "'");
        Info("row 4 = '" + lines[5].Replace("\t", "|") + "'");
        foreach (var w in t.Warnings) Info("warning: " + w);
        Check("26 rows", t.Rows.Count == 26, $"{t.Rows.Count}");
        Check("8 LookAt rows / 18 Euler rows", t.LookAtRows == 8 && t.EulerRows == 18, $"{t.LookAtRows} / {t.EulerRows}");
        Check("no row uses both parts", t.Rows.All(r => !(r.HasLookAt && r.HasCorrect && r.UsesAnyAxis)),
              $"rows with both flags: {t.Rows.Count(r => r.HasLookAt && r.HasCorrect)}");
        var cats = t.Rows.Select(r => r.Category).Distinct().OrderBy(c => c).ToArray();
        Check("8 categories 0..7", cats.SequenceEqual(Enumerable.Range(0, 8)), string.Join(",", cats));
        var groups = new[] { new[] { 0, 1, 3, 4, 5, 6 }, new[] { 7, 8, 10, 11, 12, 13 }, new[] { 17, 18 }, new[] { 23, 24 }, new[] { 2 }, new[] { 9 }, new[] { 14, 15, 16, 19 }, new[] { 20, 21, 22, 25 } };
        bool gOk = true;
        for (int c = 0; c < 8; c++) gOk &= t.RowsOf(c).SequenceEqual(groups[c]);
        Check("category grouping", gOk, string.Join(" ", Enumerable.Range(0, 8).Select(c => c + ":[" + string.Join(",", t.RowsOf(c)) + "]")));
        Info("file column 0 (older layout): " + string.Join(",", t.Rows.Select(r => r.FileCategory)));

        var r0 = t.Rows[0];
        Check("row 0: ArmUp01_dam_L from ArmUp00_L, YZX, k 0.5, X -0.66",
              r0.HasCorrect && r0.CorrectBone == "cf_J_ArmUp01_dam_L" && r0.ReferenceBone == "cf_J_ArmUp00_L" && r0.CorrectOrder == JcOrder.YZX &&
              r0.CharmRate == 0.5f && r0.Use[0] && !r0.Use[1] && !r0.Use[2] && Mathf.Approximately(r0.Min[0], -0.66f) && Mathf.Approximately(r0.Max[0], -0.66f), r0.ToString());
        var r3 = t.Rows[3];
        Check("row 3: ArmElbo_dam_01_L from ArmLow01_L, XZY, Y 0.6", r3.CorrectBone == "cf_J_ArmElbo_dam_01_L" && r3.ReferenceBone == "cf_J_ArmLow01_L" &&
              r3.CorrectOrder == JcOrder.XZY && r3.Use[1] && Mathf.Approximately(r3.Min[1], 0.6f), r3.ToString());
        var r6 = t.Rows[6];
        Check("row 6: Hand_dam_L -> Hand_Middle01_L RevX, up Hand_Wrist_dam_L Y, src Y, limit Z ZYX -180..0",
              r6.HasLookAt && r6.LookBone == "cf_J_Hand_dam_L" && r6.TargetBone == "cf_J_Hand_Middle01_L" && r6.TargetAxis == JcAxis.RevX &&
              r6.UpBone == "cf_J_Hand_Wrist_dam_L" && r6.UpAxis == JcAxis.Y && r6.SourceAxis == JcAxis.Y && r6.LimitAxis == JcAxis.Z &&
              r6.LookOrder == JcOrder.ZYX && r6.LimitMin == -180f && r6.LimitMax == 0f, r6.ToString());
        var r13 = t.Rows[13];
        Check("row 13: Hand_dam_R -> Hand_Middle01_R X, up Z, src Z, limit Z 0..180",
              r13.TargetAxis == JcAxis.X && r13.UpAxis == JcAxis.Z && r13.SourceAxis == JcAxis.Z && r13.LimitAxis == JcAxis.Z && r13.LimitMin == 0f && r13.LimitMax == 180f, r13.ToString());
        var r14 = t.Rows[14];
        Check("row 14: LegUpDam_L -> LegLow01_L RevY, up Kosi02 X, src X", r14.LookBone == "cf_J_LegUpDam_L" && r14.TargetBone == "cf_J_LegLow01_L" &&
              r14.TargetAxis == JcAxis.RevY && r14.UpBone == "cf_J_Kosi02" && r14.UpAxis == JcAxis.X && r14.SourceAxis == JcAxis.X && r14.LimitAxis == JcAxis.None, r14.ToString());
        var r18 = t.Rows[18];
        Check("row 18: LegKnee_back_L copies LegLow01_L (ZYX, 1/1/1)", r18.CorrectOrder == JcOrder.ZYX && r18.Use.All(u => u) &&
              r18.Min.All(v => v == 1f) && r18.Max.All(v => v == 1f), r18.ToString());
        var r19 = t.Rows[19];
        Check("row 19: SiriDam_L from LegUp00_L YZX 0.5/0.2/0.25", r19.CorrectOrder == JcOrder.YZX && Mathf.Approximately(r19.Min[0], 0.5f) &&
              Mathf.Approximately(r19.Min[1], 0.2f) && Mathf.Approximately(r19.Min[2], 0.25f), r19.ToString());
        Check("every Correct row is Euler with min = max", t.Rows.Where(r => r.HasCorrect && r.UsesAnyAxis).All(r => r.Calc == JcCalc.Euler &&
              Enumerable.Range(0, 3).All(a => !r.Use[a] || r.Min[a] == r.Max[a])));
        var diff = JointCorrectionTable.Diff(t, fallback);
        Check("bundle table equals the table field by field", diff == null, diff);
        return t;
    }

    // ---------------------------------------------------------------- 2. math

    static void TestMath()
    {
        sb.AppendLine();
        sb.AppendLine("2. Euler helpers");
        var rnd = new System.Random(1234);
        float RandA(double lim) => (float)((rnd.NextDouble() * 2 - 1) * lim);
        foreach (JcOrder o in Enum.GetValues(typeof(JcOrder)))
        {
            float maxErr = 0f, maxAlt = 0f, maxEul = 0f; int skipped = 0;
            for (int i = 0; i < 2000; i++)
            {
                var q = Quaternion.Euler(RandA(180), RandA(180), RandA(180));
                q = JointCorrectionMath.Normalized(q);
                var e = JointCorrectionMath.Decompose(q, o);
                JointCorrectionMath.Letters(o, out _, out int mid, out _);
                if (Mathf.Abs(e[mid]) > 89f) { skipped++; continue; }   // gimbal band (asin(0.9999) = 89.19°)
                maxErr = Mathf.Max(maxErr, Err(JointCorrectionMath.Compose(e, o), q));
                maxAlt = Mathf.Max(maxAlt, Err(JointCorrectionMath.Compose(JointCorrectionMath.Alternate(e, o), o), q));
                // Compose of known angles decomposes back to them.
                var a = new Vector3(RandA(170), RandA(170), RandA(170));
                a[mid] = RandA(85);
                var b = JointCorrectionMath.Decompose(JointCorrectionMath.Compose(a, o), o);
                maxEul = Mathf.Max(maxEul, (a - b).magnitude);
            }
            Check($"{o}: Compose(Decompose(q)) = ±q, alternate solution = ±q, Decompose(Compose(a)) = a",
                  maxErr < 1e-5f && maxAlt < 1e-5f && maxEul < 0.01f, $"max errors {maxErr:E1} / {maxAlt:E1} / {maxEul:E1}°, {skipped} in the gimbal band skipped");
        }
        var g = JointCorrectionMath.Decompose(Ry(10) * Rz(90f) * Rx(20), JcOrder.YZX);
        Check("YZX gimbal (+90°) branch: x = 0, y absorbs the outer rotation (Ry(10)Rz(90)Rx(20) -> y 30)", Mathf.Abs(g.x) < 1e-4f && Mathf.Abs(g.y - 30f) < 0.05f && Mathf.Abs(g.z - 90f) < 0.5f, $"({g.x:F3}, {g.y:F3}, {g.z:F3})");
        var gn = JointCorrectionMath.Decompose(Ry(10) * Rz(-89.995f) * Rx(20), JcOrder.YZX);
        Info($"YZX at -90° (no special case): ({gn.x:F3}, {gn.y:F3}, {gn.z:F3})");
    }

    // ---------------------------------------------------------------- 3/4. bare skeleton, worked examples

    sealed class Rig
    {
        public GameObject Go;
        public Dictionary<string, Transform> Bones;
        public Transform[] All;
        public Quaternion[] Rest;
        public Quaternion RestOf(string n) { int i = Array.IndexOf(All, Bones[n]); return Rest[i]; }
        public void Reset() { for (int i = 0; i < All.Length; i++) All[i].localRotation = Rest[i]; }
        public Quaternion this[string n] => Bones[n].localRotation;
    }

    static Rig Bare(ContentCatalog gd, List<GameObject> created)
    {
        var prefab = gd.LoadAsset<GameObject>("studio/base/00.unity3d", "p_cf_anim") ?? gd.LoadAsset<GameObject>("chara/oo_base.unity3d", "p_cf_anim");
        if (!prefab) return null;
        var go = Object.Instantiate(prefab);
        go.name = "p_cf_anim";
        created.Add(go);
        var an = go.GetComponent<Animator>();
        if (an) an.enabled = false;
        go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity;
        var rig = new Rig { Go = go, Bones = Dict(go.transform), All = go.GetComponentsInChildren<Transform>(true) };
        rig.Rest = rig.All.Select(t => t.localRotation).ToArray();
        return rig;
    }

    static JointCorrectionStage StageFor(JointCorrectionTable t, Dictionary<string, Transform> d) =>
        new JointCorrectionStage(t, n => d.TryGetValue(n, out var x) ? x : null);

    static Dictionary<string, Quaternion> e0;

    // One example: rest pose, edits as localRotation = rest · edit, all categories on, one evaluation.
    static Dictionary<string, Quaternion> RunExample(Rig rig, JointCorrectionTable t, (string bone, Quaternion edit)[] edits, bool fixWrap = false, bool robust = false)
    {
        rig.Reset();
        var st = StageFor(t, rig.Bones);
        st.FixEulerWrap = fixWrap; st.RobustLookAt = robust;
        st.EnableAll(true);
        foreach (var (bone, edit) in edits) rig.Bones[bone].localRotation = rig.RestOf(bone) * edit;
        st.Step(Frame0);
        var res = new Dictionary<string, Quaternion>();
        for (int i = 0; i < t.Rows.Count; i++) { var d = st.DrivenOf(i); if (d) res[d.name] = d.localRotation; }
        return res;
    }

    static float Expect(string label, Dictionary<string, Quaternion> res, (string bone, Quaternion q)[] exp, bool count = true)
    {
        float worst = 0f; var bad = new List<string>();
        foreach (var (bone, q) in exp)
        {
            float e = res.TryGetValue(bone, out var got) ? Err(got, q) : float.PositiveInfinity;
            worst = Mathf.Max(worst, e);
            if (!(e <= Tol)) bad.Add($"{bone} got {(res.ContainsKey(bone) ? F(res[bone]) : "missing")} want {F(q)} err {e:E2}");
        }
        if (count) maxExampleErr = Mathf.Max(maxExampleErr, worst);
        Check(label, bad.Count == 0, bad.Count == 0 ? $"{exp.Length} bones, max err {worst:E2}" : string.Join("; ", bad));
        return worst;
    }

    static (string, Quaternion)[] FromE0(params string[] bones) => bones.Select(b => (b, e0[b])).ToArray();

    static void TestExamples(ContentCatalog gd, JointCorrectionTable t, List<GameObject> created)
    {
        sb.AppendLine();
        sb.AppendLine("3. bare p_cf_anim (studio/base/00): binding and rest pose");
        var rig = Bare(gd, created);
        Check("p_cf_anim prefab loaded", rig != null);
        if (rig == null) return;
        Check("38 *_dam transforms on p_cf_anim", CountDam(rig.Go.transform) == 38, $"{CountDam(rig.Go.transform)} of {rig.All.Length} transforms");
        var headPrefab = gd.LoadAsset<GameObject>("chara/oo_base.unity3d", "p_cf_head_bone");
        if (headPrefab) Check("0 *_dam transforms on p_cf_head_bone", CountDam(headPrefab.transform) == 0, $"{CountDam(headPrefab.transform)} of {headPrefab.GetComponentsInChildren<Transform>(true).Length}");
        var st0 = StageFor(t, rig.Bones);
        Check("all 26 rows bound, 26 driven bones, no unbound name", st0.BoundRows == 26 && st0.DrivenCount == 26 && st0.Unbound.Count == 0,
              $"rows {st0.BoundRows}, driven {st0.DrivenCount}, sources {st0.SourceCount}, unbound [{string.Join(", ", st0.Unbound)}]");
        var rest = new (string, Quaternion)[]
        {
            ("cf_J_ArmUp00_L", Q(0, -0.01309f, 0, 0.99991f)), ("cf_J_ArmLow01_L", Q(0, 0.02443f, 0, 0.99970f)), ("cf_J_Hand_L", Q(0, -0.01134f, 0, 0.99994f)),
            ("cf_J_Hand_Middle01_L", Q(0, 0, 0.10453f, 0.99452f)), ("cf_J_ArmElboura_dam_L", Q(0, 0.01255f, 0, 0.99992f)), ("cf_J_ArmElboura_dam_R", Q(0, -0.01404f, 0, 0.99990f)),
            ("cf_J_LegUpDam_L", Q(-0.00720f, 0, 0.00175f, 0.99997f)), ("cf_J_LegUp00_L", Q(-0.00977f, 0, 0, 0.99995f)), ("cf_J_LegLow01_L", Q(0.03054f, 0, 0, 0.99953f)),
            ("cf_J_LegUp01_R", Q(-0.00175f, 0.00138f, 0, 1f)), ("cf_J_Hand_dam_R", Q(0, -0.00052f, 0, 1f)),
        };
        var restNow = rest.ToDictionary(r => r.Item1, r => rig[r.Item1]);
        Expect("rest rotations (spot check)", restNow, rest, count: false);
        bool posOk = (rig.Bones["cf_J_ArmUp02_dam_L"].localPosition - new Vector3(-1.145f, 0, 0)).magnitude < 1e-3f &&
                     (rig.Bones["cf_J_LegLow01_L"].localPosition - new Vector3(0, -4.2f, 0)).magnitude < 1e-3f &&
                     (rig.Bones["cf_J_Hand_Middle01_L"].localPosition - new Vector3(-0.5609f, 0, 0.0425f)).magnitude < 1e-3f;
        Check("rest positions (spot check)", posOk);

        sb.AppendLine();
        sb.AppendLine("4. worked examples (tolerance 1e-3 per component, sign-insensitive)");
        var I = Quaternion.identity;
        // E0
        e0 = RunExample(rig, t, new (string, Quaternion)[0]);
        Expect("E0 rest, every category on", e0, new (string, Quaternion)[]
        {
            ("cf_J_ArmUp01_dam_L", I), ("cf_J_ArmUp02_dam_L", I), ("cf_J_ArmUp01_dam_R", I), ("cf_J_ArmUp02_dam_R", I),
            ("cf_J_ArmLow02_dam_L", I), ("cf_J_ArmLow02_dam_R", I),
            ("cf_J_ArmElbo_dam_01_L", Q(0, 0.01466f, 0, 0.99989f)), ("cf_J_ArmElbo_dam_01_R", Q(0, -0.01466f, 0, 0.99989f)),
            ("cf_J_ArmElboura_dam_L", Q(0, 0.01412f, 0, 0.99990f)), ("cf_J_ArmElboura_dam_R", Q(0, -0.01404f, 0, 0.99990f)),
            ("cf_J_Hand_Wrist_dam_L", I), ("cf_J_Hand_Wrist_dam_R", I),
            ("cf_J_Hand_dam_L", Q(0, 0.0165f, 0, 0.99986f)), ("cf_J_Hand_dam_R", Q(0, -0.01661f, 0, 0.99986f)),
            ("cf_J_LegUpDam_L", Q(-0.0072f, 0.00001f, 0.00175f, 0.99997f)), ("cf_J_LegUpDam_R", Q(-0.00720f, 0, -0.00175f, 0.99997f)),
            ("cf_J_LegKnee_dam_L", Q(0.01527f, 0, 0, 0.99988f)), ("cf_J_LegKnee_dam_R", Q(0.01527f, 0, 0, 0.99988f)),
            ("cf_J_LegKnee_back_L", Q(0.03054f, 0, 0, 0.99953f)), ("cf_J_LegKnee_back_R", Q(0.03054f, 0, 0, 0.99953f)),
            ("cf_J_SiriDam_L", Q(-0.00489f, 0, 0, 0.99999f)), ("cf_J_SiriDam_R", Q(-0.00489f, 0, 0, 0.99999f)),
        });
        {
            // LegUp01/02: Y 0, X / Z kept from the prefab (YZX).
            var bad = new List<string>();
            foreach (var n in new[] { "cf_J_LegUp01_L", "cf_J_LegUp02_L", "cf_J_LegUp01_R", "cf_J_LegUp02_R" })
            {
                var got = JointCorrectionMath.Decompose(e0[n], JcOrder.YZX);
                var pre = JointCorrectionMath.Decompose(rig.RestOf(n), JcOrder.YZX);
                if (Mathf.Abs(got.y) > 0.01f || Mathf.Abs(got.x - pre.x) > 0.01f || Mathf.Abs(got.z - pre.z) > 0.01f) bad.Add($"{n} {got} prefab {pre}");
            }
            Check("E0 LegUp01/02 L/R: Y 0, X / Z kept from the prefab", bad.Count == 0, bad.Count == 0 ? null : string.Join("; ", bad));
            float maxDev = 0f; string worst = null;
            foreach (var kv in e0) { float a = (float)AngleDeg(kv.Value, rig.RestOf(kv.Key)); if (a > maxDev) { maxDev = a; worst = kv.Key; } }
            Check("E0: all 26 within 3.5° of the prefab value", maxDev <= 3.51f, $"max {maxDev:F3}° ({worst})");
        }
        // E1
        var e1 = RunExample(rig, t, new[] { ("cf_J_ArmUp00_L", Rx(90)) });
        Expect("E1 left upper arm +90° twist", e1, new[] { ("cf_J_ArmUp01_dam_L", Q(-0.4955f, 0, 0, 0.8686f)), ("cf_J_ArmUp02_dam_L", Q(-0.2563f, 0, 0, 0.9666f)) });
        Expect("E1 rows 4-6 unaffected", e1, FromE0("cf_J_ArmElboura_dam_L", "cf_J_Hand_Wrist_dam_L", "cf_J_Hand_dam_L"));
        Expect("E1 -90°", RunExample(rig, t, new[] { ("cf_J_ArmUp00_L", Rx(-90)) }), new[] { ("cf_J_ArmUp01_dam_L", Q(0.4955f, 0, 0, 0.8686f)), ("cf_J_ArmUp02_dam_L", Q(0.2563f, 0, 0, 0.9666f)) });
        Expect("E1 +150° (t clamped)", RunExample(rig, t, new[] { ("cf_J_ArmUp00_L", Rx(150)) }), new[] { ("cf_J_ArmUp01_dam_L", Q(-0.7604f, 0, 0, 0.6494f)), ("cf_J_ArmUp02_dam_L", Q(-0.4187f, 0, 0, 0.9081f)) });
        Expect("E1 right arm +90°", RunExample(rig, t, new[] { ("cf_J_ArmUp00_R", Rx(90)) }), new[] { ("cf_J_ArmUp01_dam_R", Q(-0.4955f, 0, 0, 0.8686f)), ("cf_J_ArmUp02_dam_R", Q(-0.2563f, 0, 0, 0.9666f)) });
        // E2
        var e2 = RunExample(rig, t, new[] { ("cf_J_ArmLow01_L", Ry(90)) });
        Expect("E2 left elbow 90°: rows 3, 4", e2, new[] { ("cf_J_ArmElbo_dam_01_L", Q(0, 0.4670f, 0, 0.8843f)), ("cf_J_ArmElboura_dam_L", Q(0, 0.4670f, 0, 0.8843f)) });
        Expect("E2 rows 5/6 as E0", e2, FromE0("cf_J_Hand_Wrist_dam_L", "cf_J_Hand_dam_L"));
        // E3
        var e3 = RunExample(rig, t, new[] { ("cf_J_LegLow01_L", Rx(90)) });
        Expect("E3 left knee 90°: rows 17, 18", e3, new[] { ("cf_J_LegKnee_dam_L", Q(0.3967f, 0, 0, 0.9179f)), ("cf_J_LegKnee_back_L", Q(0.7284f, 0, 0, 0.6852f)) });
        Expect("E3 LegUpDam_L as E0", e3, FromE0("cf_J_LegUpDam_L"));
        // E4
        var e4 = RunExample(rig, t, new[] { ("cf_J_LegUp00_L", Rx(-90)) });
        Expect("E4 left thigh flexed 90°: rows 14, 19", e4, new[] { ("cf_J_LegUpDam_L", Q(-0.5831f, 0.0013f, 0.0018f, 0.8124f)), ("cf_J_SiriDam_L", Q(-0.3872f, 0, 0, 0.9220f)) });
        Expect("E4 LegUp01/02_L stay as E0 (Y 0)", e4, FromE0("cf_J_LegUp01_L", "cf_J_LegUp02_L"));
        // E5
        Expect("E5 left thigh twisted 45°: rows 15, 16, 19", RunExample(rig, t, new[] { ("cf_J_LegUp00_L", Ry(45)) }), new[]
        {
            ("cf_J_LegUp01_L", Q(0, -0.3276f, 0, 0.9448f)), ("cf_J_LegUp02_L", Q(0, -0.1951f, 0, 0.9808f)), ("cf_J_SiriDam_L", Q(-0.0036f, 0.0785f, -0.0015f, 0.9969f)),
        });
        // E6
        Expect("E6 left hand twisted 60°: rows 2, 5, 6", RunExample(rig, t, new[] { ("cf_J_Hand_L", Rx(60)) }), new[]
        {
            ("cf_J_ArmLow02_dam_L", Q(0.2588f, 0, 0, 0.9659f)), ("cf_J_Hand_Wrist_dam_L", Q(0.5000f, 0.0001f, 0, 0.8660f)), ("cf_J_Hand_dam_L", Q(0.0003f, 0.0202f, 0, 0.9998f)),
        });
        // E7
        var w85 = new[] { ("cf_J_ArmUp01_dam_L", Q(-0.1720f, 0, 0, 0.9851f)), ("cf_J_ArmUp02_dam_L", Q(-0.0863f, 0, 0, 0.9963f)) };
        var w95 = new[] { ("cf_J_ArmUp01_dam_L", Q(-0.7604f, 0, 0, 0.6494f)), ("cf_J_ArmUp02_dam_L", Q(-0.4187f, 0, 0, 0.9081f)) };
        var r85 = RunExample(rig, t, new[] { ("cf_J_ArmUp00_L", Rz(85) * Rx(30)) });
        var r95 = RunExample(rig, t, new[] { ("cf_J_ArmUp00_L", Rz(95) * Rx(30)) });
        Expect("E7 z = 85 (flag off)", r85, w85);
        Expect("E7 z = 95 (flag off): the wrap jump is reproduced", r95, w95);
        var src95 = JointCorrectionMath.Decompose(rig.RestOf("cf_J_ArmUp00_L") * Rz(95) * Rx(30), JcOrder.YZX);
        Info($"E7 Decompose_YZX(source, z = 95) = ({src95.x:F1}, {src95.y:F1}, {src95.z:F1}) (spec: (-150.0, 178.5, 85.0))");
        double jumpOff = AngleDeg(r85["cf_J_ArmUp01_dam_L"], r95["cf_J_ArmUp01_dam_L"]);

        sb.AppendLine();
        sb.AppendLine("   fix flags");
        var f85 = RunExample(rig, t, new[] { ("cf_J_ArmUp00_L", Rz(85) * Rx(30)) }, fixWrap: true);
        var f95 = RunExample(rig, t, new[] { ("cf_J_ArmUp00_L", Rz(95) * Rx(30)) }, fixWrap: true);
        Expect("FixEulerWrap: E7 z = 85 unchanged", f85, w85, count: false);
        Expect("FixEulerWrap: E7 z = 95 continuous (= the z = 85 values)", f95, w85, count: false);
        double jumpOn = AngleDeg(f85["cf_J_ArmUp01_dam_L"], f95["cf_J_ArmUp01_dam_L"]);
        Check("wrap jump of ArmUp01_dam_L between z = 85 and 95: present with the flag off, absent with it on", jumpOff > 60 && jumpOn < 0.5,
              $"off {jumpOff:F1}°, on {jumpOn:F2}°");
        // The fixes must not change normal poses (E0-E6).
        var normal = new[]
        {
            new (string, Quaternion)[0], new[] { ("cf_J_ArmUp00_L", Rx(90)) }, new[] { ("cf_J_ArmUp00_L", Rx(150)) }, new[] { ("cf_J_ArmLow01_L", Ry(90)) },
            new[] { ("cf_J_LegLow01_L", Rx(90)) }, new[] { ("cf_J_LegUp00_L", Rx(-90)) }, new[] { ("cf_J_LegUp00_L", Ry(45)) }, new[] { ("cf_J_Hand_L", Rx(60)) },
        };
        float dFix = 0f, dRob = 0f;
        foreach (var ed in normal)
        {
            var a = RunExample(rig, t, ed); var b = RunExample(rig, t, ed, fixWrap: true); var c = RunExample(rig, t, ed, robust: true);
            foreach (var k in a.Keys) { dFix = Mathf.Max(dFix, Err(a[k], b[k])); dRob = Mathf.Max(dRob, Err(a[k], c[k])); }
        }
        Check("FixEulerWrap and RobustLookAt leave E0-E6 unchanged", dFix < 1e-5f && dRob < 1e-4f, $"max diff {dFix:E1} / {dRob:E1}");
        // Trace-only LookAt with the character yawed 180°: faithful mode still correct in this case.
        rig.Go.transform.rotation = Quaternion.AngleAxis(180f, Vector3.up);
        var yaw = RunExample(rig, t, new (string, Quaternion)[0]);
        var yawR = RunExample(rig, t, new (string, Quaternion)[0], robust: true);
        rig.Go.transform.rotation = Quaternion.identity;
        float dYaw = 0f, dYawR = 0f;
        foreach (var k in e0.Keys) { dYaw = Mathf.Max(dYaw, Err(yaw[k], e0[k])); dYawR = Mathf.Max(dYawR, Err(yawR[k], e0[k])); }
        Check("character yawed 180°: E0 local results unchanged (trace formula / robust)", dYaw < 1e-3f && dYawR < 1e-3f, $"max diff {dYaw:E2} / {dYawR:E2}");

        // Idempotence on the rest skeleton: two more evaluations do not drift.
        rig.Reset();
        var sti = StageFor(t, rig.Bones); sti.EnableAll(true);
        sti.Step(Frame0);
        var first = Enumerable.Range(0, 26).Select(i => sti.DrivenOf(i).localRotation).ToArray();
        for (int k = 0; k < 10; k++) sti.Step(Frame0);
        float drift = 0f;
        for (int i = 0; i < 26; i++) drift = Mathf.Max(drift, Err(first[i], sti.DrivenOf(i).localRotation));
        Check("no drift over 10 more evaluations without pose change", drift < 1e-5f, $"max {drift:E1}");

        // No allocation and cost.
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int k = 0; k < 2000; k++) sti.Step(Frame0);
        sw.Stop();
        long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
        Check("Step allocates nothing (2000 evaluations, all rows on)", alloc == 0, $"{alloc} bytes; {sw.Elapsed.TotalMilliseconds * 1000.0 / 2000:F1} µs per evaluation");
        rig.Reset();
    }

    // ---------------------------------------------------------------- 5. card builds

    static Card LoadCard(string path)
    {
        try { return Card.Load(path); } catch (Exception) { return null; }
    }

    static string FirstMaleCard(string dir)
    {
        if (!Directory.Exists(dir)) return null;
        foreach (var opt in new[] { SearchOption.TopDirectoryOnly, SearchOption.AllDirectories })
            foreach (var f in Directory.GetFiles(dir, "*.png", opt).OrderBy(f => f, StringComparer.Ordinal))
            {
                var c = LoadCard(f);
                if (c != null && c.Parameter.Get("sex").Int(1) == 0) return f;
            }
        return null;
    }

    static void TestBuilds(ContentCatalog gd, List<GameObject> created)
    {
        string game = CardResolveTest.GameRoot;
        string femalePath = Arg("-card", Path.Combine(game, "UserData", "chara", "female", "BR-Chan HS2 Rev2.png"));
        string malePath = Arg("-maleCard", null) ?? FirstMaleCard(Path.Combine(game, "UserData", "chara", "male"));

        sb.AppendLine();
        sb.AppendLine($"5. female build: {Path.GetFileName(femalePath)}");
        var fc = LoadCard(femalePath);
        Check("female card loads", fc != null, femalePath.Replace(game, "<game>"));
        if (fc != null) TestBuild(gd, fc, created, full: true);

        sb.AppendLine();
        sb.AppendLine($"6. male build: {(malePath != null ? Path.GetFileName(malePath) : "-")}");
        var mc = malePath != null ? LoadCard(malePath) : null;
        Check("male card found and loads", mc != null, malePath?.Replace(game, "<game>"));
        if (mc != null) TestBuild(gd, mc, created, full: false);
    }

    static void TestBuild(ContentCatalog gd, Card card, List<GameObject> created, bool full)
    {
        var asm = new CharaAssembler(gd);
        var chara = asm.BuildFromDocuments(card);
        created.Add(chara);
        var ctx = asm.Context;
        Check("built" + (asm.IsMale ? " (male path)" : ""), chara != null && ctx != null);
        foreach (var l in asm.Log.Where(l => l.StartsWith("correct:") || l.StartsWith("motion:"))) Info(l);
        var motion = ctx.Motion;
        var st = motion.Get<JointCorrectionStage>();
        Check("stage attached at MotionPhase.Correct", st != null && st.Phase == MotionPhase.Correct);
        if (st == null) return;
        Check("table from cf_expression (female table for both sexes)", st.Table.Source == JointCorrectionTable.FemaleAsset, st.Table.Source);
        var anim = Dict(ctx.Anim.transform);
        var names = st.Table.Rows.SelectMany(r => new[] { r.LookBone, r.TargetBone, r.UpBone, r.CorrectBone, r.ReferenceBone }).Where(n => n != null).Distinct().ToList();
        var absent = names.Where(n => !anim.ContainsKey(n)).ToList();
        Check($"all {names.Count} table bone names exist on p_cf_anim", absent.Count == 0, absent.Count == 0 ? null : string.Join(", ", absent));
        Check("26 rows bound, 26 driven bones, nothing unbound", st.BoundRows == 26 && st.DrivenCount == 26 && st.Unbound.Count == 0,
              $"rows {st.BoundRows}, driven {st.DrivenCount}, sources {st.SourceCount}, unbound [{string.Join(", ", st.Unbound)}]");
        bool inAnim = Enumerable.Range(0, 26).All(i => st.DrivenOf(i) && st.DrivenOf(i).IsChildOf(ctx.Anim.transform));
        Check("driven bones resolved inside p_cf_anim", inAnim);
        int dam = CountDam(ctx.Anim.transform), damHead = ctx.HeadBone ? CountDam(ctx.HeadBone.transform) : -1;
        Check("38 *_dam transforms on p_cf_anim, 0 on p_cf_head_bone (build)", dam == 38 && damHead == 0, $"{dam} / {damHead}");
        var facade = CharaJointCorrection.Of(chara);
        Check("facade found; new character: all 8 categories off", facade != null && facade.SceneFlags().All(b => !b) &&
              Enumerable.Range(0, 26).All(i => !st.IsRowEnabled(i)));
        if (!full || facade == null) return;

        // Only the joint correction runs in these checks (the other stages are switched off).
        var others = motion.Stages.Where(s => s != st).ToList();
        foreach (var s in others) s.Enabled = false;
        Info($"other stages switched off for the checks: {(others.Count == 0 ? "none" : string.Join(", ", others.Select(s => s.Name)))}");
        var all = ctx.Anim.GetComponentsInChildren<Transform>(true);
        Quaternion[] Snap() => all.Select(x => x.localRotation).ToArray();
        void Put(Quaternion[] s) { for (int i = 0; i < all.Length; i++) all[i].localRotation = s[i]; }
        var driven = Enumerable.Range(0, 26).Select(i => st.DrivenOf(i)).ToArray();
        Quaternion[] D() => driven.Select(x => x.localRotation).ToArray();

        // all off
        var restSnap = Snap();
        var d0 = D();
        motion.Step(1f / 30f);
        var d1 = D();
        Check("all categories off: driven bones untouched, no evaluation", Enumerable.Range(0, 26).All(i => JointCorrectionMath.Same(d0[i], d1[i])) && st.Evaluations == 0,
              $"evaluations {st.Evaluations}");

        // a pose that moves every source bone
        var b = ctx.BodyBones;
        void Edit(string n, Quaternion e) => b[n].localRotation = b[n].localRotation * e;
        Edit("cf_J_ArmUp00_L", Rz(20) * Rx(40)); Edit("cf_J_ArmUp00_R", Rz(-20) * Rx(-35));
        Edit("cf_J_ArmLow01_L", Ry(50)); Edit("cf_J_ArmLow01_R", Ry(-45));
        Edit("cf_J_Hand_L", Rx(40) * Rz(15)); Edit("cf_J_Hand_R", Rx(-30) * Rz(-10));
        Edit("cf_J_LegUp00_L", Rx(-40) * Ry(30)); Edit("cf_J_LegUp00_R", Rx(-30) * Ry(-25));
        Edit("cf_J_LegLow01_L", Rx(60)); Edit("cf_J_LegLow01_R", Rx(45));
        var pose = Snap();
        var dp = D();
        var bad = new List<string>();
        for (int c = 0; c < 8; c++)
        {
            Put(pose);
            st.EnableCategory(c, true);
            motion.Step(1f / 30f);
            var dc = D();
            for (int i = 0; i < 26; i++)
            {
                bool mine = st.Table.Rows[i].Category == c;
                double ang = AngleDeg(dc[i], dp[i]);
                if (mine && ang < 0.01) bad.Add($"cat {c} row {i} {driven[i].name} unchanged");
                if (!mine && !JointCorrectionMath.Same(dc[i], dp[i])) bad.Add($"cat {c} touched row {i} {driven[i].name} ({ang:F3}°)");
            }
            st.EnableCategory(c, false);
            var dr = D();
            for (int i = 0; i < 26; i++) if (!JointCorrectionMath.Same(dr[i], dp[i])) bad.Add($"cat {c} off: row {i} {driven[i].name} not restored");
        }
        Check("per-category toggle changes exactly its own rows; switching it off restores them (RestoreOnDisable)", bad.Count == 0,
              bad.Count == 0 ? "8 categories" : string.Join("; ", bad.Take(8)));

        // RestoreOnDisable off: the corrected value stays (faithful to a game with no writer).
        Put(pose);
        st.RestoreOnDisable = false;
        st.EnableCategory(0, true); motion.Step(1f / 30f);
        var kept = st.DrivenOf(0).localRotation;
        st.EnableCategory(0, false);
        Check("RestoreOnDisable = false keeps the corrected value", JointCorrectionMath.Same(kept, st.DrivenOf(0).localRotation) && AngleDeg(kept, dp[0]) > 0.01);
        st.RestoreOnDisable = true;

        // Step(0) and paused steps evaluate (pose-driven: the stage runs every LateUpdate regardless of time)
        Put(pose);
        st.EnableCategory(0, true);
        int ev = st.Evaluations;
        motion.Step(0f);
        bool z1 = st.Evaluations == ev + 1 && AngleDeg(st.DrivenOf(0).localRotation, dp[0]) > 0.01;
        Put(pose);
        motion.Paused = true; motion.Step(1f / 30f); motion.Paused = false;
        bool z2 = st.Evaluations == ev + 2 && AngleDeg(st.DrivenOf(0).localRotation, dp[0]) > 0.01;
        Check("Step(0) and a paused step still evaluate", z1 && z2, $"evaluations {st.Evaluations - ev}");

        // ResetAll harmless
        st.EnableAll(true);
        Put(pose); motion.Step(1f / 30f);
        var a1 = D();
        motion.ResetAll();
        motion.Step(1f / 30f);
        var a2 = D();
        float dr2 = 0f;
        for (int i = 0; i < 26; i++) dr2 = Mathf.Max(dr2, Err(a1[i], a2[i]));
        Check("ResetAll harmless (same result after a reset, no stage errors)", dr2 < 1e-5f && motion.Errors == 0, $"max diff {dr2:E1}, errors {motion.Errors}");

        // FK-style edit + Refresh through the facade (the dam values follow in the same frame).
        facade.ApplySceneFlags(new bool[8]);
        facade[0] = true;
        Put(restSnap);
        b["cf_J_ArmUp00_L"].localRotation = b["cf_J_ArmUp00_L"].localRotation * Rx(90);
        facade.Refresh();
        var x01 = JointCorrectionMath.Decompose(b["cf_J_ArmUp01_dam_L"].localRotation, JcOrder.YZX).x;
        Check("facade: FK twist 90° + Refresh -> ArmUp01_dam_L X ≈ -59.4°", Mathf.Abs(x01 + 59.4f) < 0.3f, $"{x01:F2}° (the card's shape pose)");
        facade.ApplySceneFlags(new[] { true, true, true, true, true, true, true, true });
        bool sceneOk = facade.SceneFlags().All(v => v);
        facade.ApplyPoseFlags(new[] { false, false, false, false });
        var sf = facade.SceneFlags();
        bool poseOk = sf.Take(4).All(v => !v) && sf.Skip(4).All(v => v) && facade.PoseFlags().Length == 4;
        Check("facade: scene flags store 8; pose flags store / restore only categories 0..3", sceneOk && poseOk, string.Join("", sf.Select(v => v ? '1' : '0')));
        facade.ApplySceneFlags(null);
        Put(restSnap);
        foreach (var s in others) s.Enabled = true;
    }
}
