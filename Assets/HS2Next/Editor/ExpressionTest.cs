// Batch test of the expression stage (Runtime/Motion/Face).
// Run: -executeMethod ExpressionTest.Run [-card <png>] [-male <png>] [-render]
// Default cards: <game>/UserData/chara/female/BR-Chan HS2 Rev2.png and the first card (recursive,
// sorted by relative path) of <game>/UserData/chara/male. Characters are stepped by hand
// (CharaMotion.Step); no play loop is needed.
//   1. data checks / Appendix A-B (pattern counts, targets, blend-shape counts,
//      blink and mouth-width values, full index tables of the vanilla heads);
//   2. worked examples with exact integer weights, the float32 truncation sweep (0.53 → 52, 0.59 → 58);
//   3. blink timeline with scripted draws and a seeded 60 s run (phase durations within one
//      frame at 1/60), eyebrow sync, cross-fade, blinking off (v²), fixed mouth, mouth width,
//      tears / cheek / highlight materials, Studio UI index ↔ id (female and male), out-of-range
//      rejection, Step(0) holds, ResetAll, eyelid correction (off by default);
//   4. -render (graphics device; female reference card only, the male card is skipped): URP face
//      close-ups to Spike/out/motion/face_<case>.png, rendered like CharaRenderTest.
// Report: Spike/out/motion/face_report.txt (PASS / FAIL lines with numbers); exit code 1 on any FAIL.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Chara;
using HS2Next.Content;
using HS2Next.Data;
using HS2Next.IO;
using HS2Next.Motion;
using HS2Next.Motion.Face;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class ExpressionTest
{
    static StringBuilder sb;
    static int passes, fails;
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "motion");
    const float F60 = 1f / 60f;

    static string Arg(string name, string def)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    static void Check(string name, bool ok, string detail = "")
    {
        if (ok) passes++; else fails++;
        sb.AppendLine($"{(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? ": " + detail : "")}");
    }
    static void Info(string s) => sb.AppendLine("     " + s);
    static void Section(string s) => sb.AppendLine().AppendLine("== " + s);

    sealed class Chara
    {
        public GameObject Go;
        public CharaContext Ctx;
        public CharaExpression Ex;
        public int HeadId;
        public string Label;
        public ExpressionStage St => Ex.Stage;
        public SkinnedMeshRenderer R(string name) => CharaAssembler.Find(Ctx.Head.transform, name)?.GetComponent<SkinnedMeshRenderer>();
        public float W(string r, int i) => R(r).GetBlendShapeWeight(i);
        public void Step(float dt) => Ctx.Motion.Step(dt);
    }

    public static void Run()
    {
        sb = new StringBuilder(); passes = fails = 0;
        Directory.CreateDirectory(OutDir);
        var head = new StringBuilder();
        ContentCatalog gd = null;
        try
        {
            bool render = TestArgs.Get().Contains("-render");
            if (render) ShaderUtil.allowAsyncCompilation = false;
            gd = CardResolveTest.BuildDb(head);
            sb.Append(head);
            string femalePath = Arg("-card", Path.Combine(CardResolveTest.GameRoot, "UserData", "chara", "female", "BR-Chan HS2 Rev2.png"));
            string maleDir = Path.Combine(CardResolveTest.GameRoot, "UserData", "chara", "male");
            string malePath = Arg("-male", null) ?? (Directory.Exists(maleDir)
                ? Directory.GetFiles(maleDir, "*.png", SearchOption.AllDirectories)
                    .OrderBy(f => f.Substring(maleDir.Length).Replace('\\', '/'), StringComparer.Ordinal).FirstOrDefault()
                : null);

            FaceSetup.Seed = 12345;
            var f = Build(gd, femalePath, "female");
            Section("1. data (female)");
            DataChecks(f);
            Section($"2. worked examples (female head {f.HeadId})");
            WorkedExamples(f);
            Section("3. behaviour (female)");
            BlinkScripted();
            BlinkSeeded(f);
            Behaviour(f);
            Materials(f, gd, femalePath);
            UiMapping(f);
            Rejection(f);
            HoldAndReset(f);
            Correction(f);

            if (render)
            {
                Section("4. renders (URP)");
                Render(f);
                Info("male card skipped in -render runs (render checks use the female reference card only)");
            }
            else if (malePath != null)
            {
                var m = Build(gd, malePath, "male");
                Section("1. data (male)");
                DataChecks(m);
                Section("3. UI mapping (male)");
                UiMapping(m);
                Object.DestroyImmediate(m.Go);
            }
            else Check("male card", false, "no male card found");
            Object.DestroyImmediate(f.Go);
        }
        catch (Exception e)
        {
            Check("exception", false, e.ToString());
        }
        finally
        {
            FaceSetup.Seed = null;
            GraphicsSettings.defaultRenderPipeline = null; QualitySettings.renderPipeline = null;
        }
        sb.Insert(0, $"ExpressionTest: {passes} PASS, {fails} FAIL\n");
        File.WriteAllText(Path.Combine(OutDir, "face_report.txt"), sb.ToString());
        gd?.UnloadAll();
        Debug.Log($"EXPRESSION TEST DONE: {passes} PASS, {fails} FAIL");
        TestExit.Exit(fails > 0 ? 1 : 0);
    }

    static Chara Build(ContentCatalog gd, string path, string label)
    {
        var card = Card.Load(path);
        var asm = new CharaAssembler(gd);
        var go = asm.BuildFromDocuments(card);
        var c = new Chara { Go = go, Ctx = asm.Context, Ex = CharaExpression.Of(go), HeadId = card.Face.Get("headId").Int(), Label = label };
        sb.AppendLine($"built {label} '{card.Name}' ({Path.GetFileName(path)}), head id {c.HeadId}, {(asm.IsMale ? "male" : "female")}");
        foreach (var l in asm.Log.Where(l => l.StartsWith("face") || l.StartsWith("motion") || l.StartsWith("head:"))) Info(l);
        if (c.Ex == null) throw new Exception($"{label}: no expression stage attached");
        return c;
    }

    // Studio defaults, blinking off, no voice, cross-fades finished, one computation.
    static void Neutral(Chara c)
    {
        var ex = c.Ex;
        ex.StartStudio();
        ex.SetVoicePlaying(false);
        ex.SetLipSync(true);
        ex.SetMouthOpen(0f);
        ex.Blink = false;
        c.St.Width.Restart();
        c.St.FinishFades();
        c.Step(F60);
    }

    // ------------------------------------------------------------------ 1. data

    static readonly int[] A1Eyebrow = { 0, 16, 17, 18, 19, 20, 21, 22, 20, 20, 20, 23, 0, 0, 25, 24 };
    static readonly int[] A2EyesClose = { 1, 5, 12, 12, 15, 12, 12, 6, 6, 6, 12, 1, 1, 1, 12, 12 };
    static readonly int[] A2EyesOpen = { 0, 4, 13, 13, 14, 13, 13, 6, 7, 8, 13, 9, 2, 3, 10, 11 };
    static readonly int[] A3HeadClose = { 0, 26, 28, 30, 32, 34, 36, 38, 40, 57, 42, 43, 44, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 26, 32 };
    static readonly int[] A3HeadOpen = { 42, 27, 29, 31, 33, 35, 37, 42, 42, 57, 42, 43, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 29, 37 };
    static readonly int[] A3ToothClose = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 1, 4, 5, 5, 5, 6, 3, 2, 0, 0, 1, 1, 0, 0 };
    static readonly int[] A3ToothOpen = { 2, 1, 2, 1, 1, 2, 2, 1, 1, 1, 2, 3, 2, 4, 5, 5, 5, 3, 3, 2, 0, 0, 1, 1, 2, 2 };
    static readonly int[] A3TangClose = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 10, 2, 3, 1, 4, 5, 6, 7, 8, 9, 1, 1, 1, 1, 1, 0, 0 };
    static readonly int[] A3TangOpen = { 2, 1, 2, 1, 1, 2, 2, 1, 1, 10, 2, 3, 2, 4, 5, 6, 7, 8, 9, 1, 1, 1, 1, 1, 2, 2 };
    // Appendix B: o_namida as a mouth target (58 shapes: male 00 and female 01 have the same table).
    static readonly int[] BNamidaClose = { 0, 26, 28, 0, 32, 0, 36, 38, 40, 57, 42, 43, 0, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 26, 32 };
    static readonly int[] BNamidaOpen = { 42, 27, 29, 31, 33, 35, 37, 42, 42, 57, 42, 43, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 29, 37 };
    // Appendix B, female p_cf_head_01: o_eyelashes (e00_defo stored last), o_tooth / o_tang (k00_defo stored last).
    static readonly int[] BF1LashClose = { 0, 4, 11, 11, 14, 11, 11, 5, 5, 5, 11, 0, 0, 0, 11, 11 };
    static readonly int[] BF1LashOpen = { 15, 3, 12, 12, 13, 12, 12, 5, 6, 7, 12, 8, 1, 2, 9, 10 };
    static readonly int[] BF1ToothClose = { 6, 6, 6, 6, 6, 6, 6, 6, 6, 0, 1, 2, 0, 3, 4, 4, 4, 5, 2, 1, 6, 6, 0, 0, 5, 5 };
    static readonly int[] BF1ToothOpen = { 1, 0, 1, 0, 0, 1, 1, 0, 0, 0, 1, 2, 1, 3, 4, 4, 4, 2, 2, 1, 6, 6, 0, 0, 1, 4 };
    static readonly int[] BF1TangClose = { 10, 10, 10, 10, 10, 10, 10, 10, 10, 9, 1, 2, 0, 3, 4, 5, 6, 7, 8, 0, 0, 0, 0, 0, 10, 10 };
    static readonly int[] BF1TangOpen = { 1, 0, 1, 0, 0, 1, 1, 0, 0, 9, 1, 2, 1, 3, 4, 5, 6, 7, 8, 0, 0, 0, 0, 0, 1, 1 };
    const string A4Head = "e00_defo e01_close e02_close_R e03_close_L e04_warai e04_warai_cl e05_damage e06_damage_R e07_damage_L e08_odoroki e09_wink_R e10_wink_L e11_wink_cl e12_wink_half e13_ikari e14_ikari_cl g01_warai1 g02_warai2 g03_ikari1 g04_ikari2 g05_kanasimi1 g06_kanasimi2 g07_damage g08_up g09_up_R g10_up_L k01_warai1 k01_warai1_op k02_warai2 k02_warai2_op k03_kanasimi k03_kanasimi_op k04_ikari k04_ikari_op k05_fukure k05_fukure_op k06_damage k06_damage_op k07_rightup k07_rightup_op k08_leftup k08_leftup_op k09_open1 k10_open2 k11_kiss k11_kiss_op k12_fera k13_sitadasi1 k14_sitadasi2 k15_sitadasi3 k16_ahe1 k17_ahe2 k18_a k19_i k20_u k21_e k22_o k23_tehepero";

    static void DataChecks(Chara c)
    {
        var rig = c.St.Rig;
        string L = c.Label;
        Info($"component {rig.ComponentType}; head id {c.HeadId}");
        string Names(FbsControlData d) => string.Join(",", d.Targets.Select(t => t.Name));
        string Shapes(FbsControlData d) => string.Join(",", d.Targets.Select(t => t.BlendShapeCount));
        Info($"eyebrow targets {Names(rig.Eyebrow)} shapes {Shapes(rig.Eyebrow)}; eyes {Names(rig.Eyes)} shapes {Shapes(rig.Eyes)}; mouth {Names(rig.Mouth)} shapes {Shapes(rig.Mouth)}");
        Info($"blink F {rig.BlinkFrequency} speed {rig.BaseSpeed}; SyncBlink {rig.Eyebrow.SyncBlink}; eyes open {rig.Eyes.OpenMin}/{rig.Eyes.OpenMax}/{rig.Eyes.FixedRate}; " +
             $"mouth width use {rig.Mouth.UseAdjustWidthScale} time {rig.Mouth.RandTimeMin}/{rig.Mouth.RandTimeMax} scale {rig.Mouth.RandScaleMin}/{rig.Mouth.RandScaleMax} ref {rig.Mouth.OpenRefValue}; " +
             $"eye look ctrl {rig.HasEyeLookController}; correct {rig.EyeLookUpCorrect}/{rig.EyeLookDownCorrect}/{rig.EyeLookSideCorrect}");

        // Generic consistency (any head): every target has the pattern count of target 0 and indices inside its mesh.
        bool consistent = true; string bad = "";
        foreach (var (n, d) in new[] { ("eyebrow", rig.Eyebrow), ("eyes", rig.Eyes), ("mouth", rig.Mouth) })
            foreach (var t in d.Targets)
            {
                if (t.PatternCount != d.PatternCount) { consistent = false; bad += $" {n}/{t.Name} count {t.PatternCount}"; }
                for (int p = 0; p < t.PatternCount; p++)
                    if (t.Close[p] >= t.BlendShapeCount || t.Open[p] >= t.BlendShapeCount || t.Close[p] < -1 || t.Open[p] < -1) { consistent = false; bad += $" {n}/{t.Name}/{p}"; }
            }
        Check($"{L} tables consistent (equal counts, indices inside the meshes)", consistent, bad.Trim());
        Check($"{L} blink values", rig.BlinkFrequency == 30 && Mathf.Approximately(rig.BaseSpeed, 0.15f), $"F {rig.BlinkFrequency}, speed {rig.BaseSpeed}");
        Check($"{L} eye-look controller null, correction off", !rig.HasEyeLookController && !c.St.EyelidCorrection);
        Check($"{L} width flag after load (pattern 0 → on)", c.St.Width.Enabled, $"serialized {rig.Mouth.UseAdjustWidthScale}");
        Check($"{L} cf_J_MouthMove found", c.St.MouthWidthBone, c.St.MouthWidthBone ? $"scale x {c.St.MouthWidthBone.localScale.x}" : "missing");

        bool male = c.Ctx.IsMale;
        bool f0 = !male && c.HeadId == 0, f1 = !male && c.HeadId == 1, m0 = male && c.HeadId == 0;
        if (!f0 && !f1 && !m0) { Info($"{L}: head {c.HeadId} has no per-head table in this test (Appendix B: female 0..3, male 0); per-head checks skipped"); return; }
        bool namidaMouth = !f0;   // the mouth also drives o_namida on male 00 and female 01, 02
        Check($"{L} pattern counts 16/16/26", rig.Eyebrow.PatternCount == 16 && rig.Eyes.PatternCount == 16 && rig.Mouth.PatternCount == 26,
            $"{rig.Eyebrow.PatternCount}/{rig.Eyes.PatternCount}/{rig.Mouth.PatternCount}");
        Check($"{L} eyebrow targets", Names(rig.Eyebrow) == "o_head", Names(rig.Eyebrow));
        Check($"{L} eyes targets", Names(rig.Eyes) == "o_head,o_eyelashes,o_namida", Names(rig.Eyes));
        string mouthT = namidaMouth ? "o_head,o_tooth,o_tang,o_namida" : "o_head,o_tooth,o_tang";
        Check($"{L} mouth targets", Names(rig.Mouth) == mouthT, Names(rig.Mouth));
        var counts = new Dictionary<string, int> { { "o_head", 58 }, { "o_eyelashes", 16 }, { "o_namida", f0 ? 37 : 58 }, { "o_tooth", 7 }, { "o_tang", 11 } };
        string got = string.Join(" ", counts.Keys.Select(k => $"{k} {c.R(k)?.sharedMesh?.blendShapeCount}"));
        Check($"{L} blend-shape counts", counts.All(kv => c.R(kv.Key) && c.R(kv.Key).sharedMesh.blendShapeCount == kv.Value), got);
        float eyesMax = f1 ? 0.9f : 1f;
        Check($"{L} control values (eyes OpenMax {eyesMax})",
            rig.Eyebrow.SyncBlink && rig.Eyebrow.OpenMin == 0 && rig.Eyebrow.OpenMax == 1 && Mathf.Approximately(rig.Eyebrow.FixedRate, -0.1f)
            && rig.Eyes.OpenMin == 0 && Mathf.Approximately(rig.Eyes.OpenMax, eyesMax) && Mathf.Approximately(rig.Eyes.FixedRate, -0.1f)
            && rig.Mouth.OpenMin == 0 && rig.Mouth.OpenMax == 1 && Mathf.Approximately(rig.Mouth.FixedRate, -0.1f)
            && !rig.Mouth.ObjAdjustWidthScale && Mathf.Approximately(rig.Mouth.OpenRefValue, 0.2f));
        Check($"{L} eyes OpenMax after the status is applied = 1 (the serialized value never takes effect)", c.St.Eyes.OpenMax == 1f, $"{c.St.Eyes.OpenMax}");
        float t0 = male ? 0.10f : 0.65f, t1 = male ? 0.20f : 0.80f, s0 = male ? 0.65f : 0.90f;
        Check($"{L} mouth width values",
            rig.Mouth.UseAdjustWidthScale == !male && Mathf.Approximately(rig.Mouth.RandTimeMin, t0) && Mathf.Approximately(rig.Mouth.RandTimeMax, t1)
            && Mathf.Approximately(rig.Mouth.RandScaleMin, s0) && Mathf.Approximately(rig.Mouth.RandScaleMax, 1f),
            $"use {rig.Mouth.UseAdjustWidthScale}, time {rig.Mouth.RandTimeMin}..{rig.Mouth.RandTimeMax}, scale {rig.Mouth.RandScaleMin}..{rig.Mouth.RandScaleMax}");
        float cu = f1 ? 0.069f : 0.1f, cd = f1 ? 0.15f : 0.3f, cs = f1 ? 0.306f : 0.1f;
        Check($"{L} eyelid correction constants {cu}/{cd}/{cs}", Mathf.Approximately(rig.EyeLookUpCorrect, cu) && Mathf.Approximately(rig.EyeLookDownCorrect, cd) && Mathf.Approximately(rig.EyeLookSideCorrect, cs),
            $"{rig.EyeLookUpCorrect}/{rig.EyeLookDownCorrect}/{rig.EyeLookSideCorrect}");

        // Full tables (Appendix A, with the Appendix B differences of the head).
        bool Table(FbsTargetData t, int[] close, int[] open, out string diff)
        {
            diff = "";
            for (int p = 0; p < close.Length; p++)
                if (p >= t.PatternCount || t.Close[p] != close[p] || t.Open[p] != open[p])
                    diff += $" {p}:({(p < t.PatternCount ? t.Close[p] + "," + t.Open[p] : "-")})";
            return diff.Length == 0 && t.PatternCount == close.Length;
        }
        string d1, tag = f0 ? "A" : "A/B";
        Check($"{L} {tag} eyebrow table", Table(rig.Eyebrow.Targets[0], A1Eyebrow, A1Eyebrow, out d1), d1);
        Check($"{L} {tag} eyes table o_head", Table(rig.Eyes.Targets[0], A2EyesClose, A2EyesOpen, out d1), d1);
        if (f1) Check($"{L} B eyes table o_eyelashes (head 01 order)", Table(rig.Eyes.Targets[1], BF1LashClose, BF1LashOpen, out d1), d1);
        else Check($"{L} A eyes table o_eyelashes", Table(rig.Eyes.Targets[1], A2EyesClose, A2EyesOpen, out d1), d1);
        Check($"{L} {tag} eyes table o_namida", Table(rig.Eyes.Targets[2], A2EyesClose, A2EyesOpen, out d1), d1);
        Check($"{L} {tag} mouth table o_head", Table(rig.Mouth.Targets[0], A3HeadClose, A3HeadOpen, out d1), d1);
        int[] tc, to, gc, go;
        if (f1) { tc = BF1ToothClose; to = BF1ToothOpen; gc = BF1TangClose; go = BF1TangOpen; }
        else
        {
            tc = (int[])A3ToothClose.Clone(); to = (int[])A3ToothOpen.Clone(); gc = A3TangClose; go = A3TangOpen;
            if (male) { tc[24] = 6; to[24] = 2; tc[25] = 6; to[25] = 5; }
        }
        Check($"{L} {tag} mouth table o_tooth", Table(rig.Mouth.Targets[1], tc, to, out d1), d1);
        Check($"{L} {tag} mouth table o_tang", Table(rig.Mouth.Targets[2], gc, go, out d1), d1);
        if (namidaMouth) Check($"{L} B mouth table o_namida", Table(rig.Mouth.Targets[3], BNamidaClose, BNamidaOpen, out d1), d1);
        var mesh = c.R("o_head").sharedMesh;
        var names = A4Head.Split(' ');
        string nd = "";
        for (int i = 0; i < names.Length && i < mesh.blendShapeCount; i++)
            if (!mesh.GetBlendShapeName(i).EndsWith(names[i], StringComparison.Ordinal)) nd += $" {i}:{mesh.GetBlendShapeName(i)}";
        Check($"{L} A.4 o_head blend-shape names (identical on all heads)", nd.Length == 0, nd.Length == 0 ? $"e.g. [42] {mesh.GetBlendShapeName(42)}" : nd);
        if (f1)
        {
            string n1 = c.R("o_eyelashes").sharedMesh.GetBlendShapeName(15), n2 = c.R("o_tooth").sharedMesh.GetBlendShapeName(6), n3 = c.R("o_tang").sharedMesh.GetBlendShapeName(10);
            Check($"{L} head 01 order: e00_defo / k00_defo stored last", n1.EndsWith("e00_defo") && n2.EndsWith("k00_defo") && n3.EndsWith("k00_defo"), $"{n1}, {n2}, {n3}");
        }
    }

    // ------------------------------------------------------------------ 2. worked examples

    // Expected weights by shape name (prefix-free, resolved on each renderer, so the female 00 examples also
    // check heads with another blend-shape order); every other index that any control's table writes on that renderer must be 0.
    static void Expect(string name, Chara c, string r, params (string shape, float w)[] want)
    {
        var smr = c.R(r);
        var mesh = smr.sharedMesh;
        string bad = "", ok = "";
        var named = new HashSet<int>();
        foreach (var (shape, w) in want)
        {
            int i = ShapeIndex(mesh, shape);
            if (i < 0) { bad += $" {shape} missing"; continue; }
            named.Add(i);
            float v = smr.GetBlendShapeWeight(i);
            ok += $" [{i}] {shape}={v}";
            if (v != w) bad += $" [{i}] {shape}={v} (want {w})";
        }
        var st = c.St;
        foreach (var ctl in new[] { st.Eyebrow, st.Eyes, st.Mouth })
            for (int t = 0; t < ctl.TargetCount; t++)
            {
                var td = ctl.TargetData(t);
                if (td.Renderer != smr) continue;
                foreach (int i in td.Close.Concat(td.Open).Distinct())
                    if (i >= 0 && !named.Contains(i) && smr.GetBlendShapeWeight(i) != 0f) { bad += $" [{i}]={smr.GetBlendShapeWeight(i)} (want 0)"; named.Add(i); }
            }
        Check(name, bad.Length == 0, bad.Length == 0 ? ok.Trim() + ", rest of the tables 0" : bad.Trim());
    }

    static IEnumerable<int> R(int a, int b) => Enumerable.Range(a, b - a + 1);

    static int ShapeIndex(Mesh m, string shape)
    {
        for (int i = 0; i < m.blendShapeCount; i++)
        {
            string n = m.GetBlendShapeName(i);
            if (n == shape || n.EndsWith("." + shape, StringComparison.Ordinal)) return i;
        }
        return -1;
    }

    static void WorkedExamples(Chara c)
    {
        var st = c.St; var ex = c.Ex;
        bool namidaMouth = st.Rig.Mouth.Targets.Any(t => t.Name == "o_namida");
        float nd = namidaMouth ? 100f : 52f;   // o_namida e00_defo: owned by the mouth where it drives o_namida
        Info($"head {c.HeadId}: o_head tables as female 00; other renderers checked by shape name{(namidaMouth ? "; the mouth also writes o_namida" : "")}");
        //
        Neutral(c);
        st.Eyes.OpenRate = 0.53f; c.Step(F60);
        Check("18.1 n for openness 0.53", st.Eyes.LastN == 52, $"n {st.Eyes.LastN}");
        Expect("18.1 o_head (mouth owns e00_defo)", c, "o_head", ("e01_close", 48), ("e00_defo", 100));
        Expect("18.1 o_eyelashes", c, "o_eyelashes", ("e01_close", 48), ("e00_defo", 52));
        Expect("18.1 o_namida", c, "o_namida", ("e01_close", 48), ("e00_defo", nd));
        st.Eyes.OpenRate = 0.59f; c.Step(F60);
        Expect("float32 0.59 → 58 (o_eyelashes)", c, "o_eyelashes", ("e01_close", 42), ("e00_defo", 58));
        var odd = new List<int>();
        for (int k = 0; k <= 100; k++) { st.Eyes.OpenRate = k / 100f; c.Step(0f); if (st.Eyes.LastN != k) odd.Add(k); }
        Check("float32 sweep k/100: only 53 and 59 truncate to k-1", odd.SequenceEqual(new[] { 53, 59 }), string.Join(",", odd));

        //
        Neutral(c);
        ex.SetStudioMouth(1); ex.SetMouthOpen(0.37f); c.Step(F60);
        Check("18.2 FixedRate 0.37, n 37", st.Mouth.FixedRate == 0.37f && st.Mouth.LastN == 37, $"fixed {st.Mouth.FixedRate}, n {st.Mouth.LastN}");
        Expect("18.2 o_head (e00_defo ends at 0)", c, "o_head", ("k01_warai1", 63), ("k01_warai1_op", 37));
        Expect("18.2 o_tooth", c, "o_tooth", ("k00_defo", 63), ("k01_open0", 37));
        Expect("18.2 o_tang", c, "o_tang", ("k00_defo", 63), ("k01_open0", 37));
        if (namidaMouth) Expect("18.2 o_namida (mouth target on this head)", c, "o_namida", ("k01_warai1", 63), ("k01_warai1_op", 37));
        Check("18.2 width 1 without voice", st.MouthWidthBone.localScale.x == 1f && st.Width.AdjustWidthScale == 1f, $"bone x {st.MouthWidthBone.localScale.x}");

        //
        Neutral(c);
        ex.SetEyebrowUiIndex(1); c.Step(F60);
        Check("18.3 first frame b = dt/0.15", Mathf.Abs(st.Eyebrow.LastBlend - F60 / 0.15f) < 1e-5f && Mathf.Abs(c.W("o_head", 16) - 100f * F60 / 0.15f) < 1e-3f,
            $"b {st.Eyebrow.LastBlend:0.0000}, [16] {c.W("o_head", 16):0.000}");
        Neutral(c);
        ex.ChangeEyebrowPtn(1); c.Step(0.03f); c.Step(0.03f);
        Check("18.3 b 0.4: [16] 40, other eyebrow indices 0, [0] owned by the mouth (100)",
            Mathf.Abs(st.Eyebrow.LastBlend - 0.4f) < 1e-5f && Mathf.Abs(c.W("o_head", 16) - 40f) < 1e-3f && R(17, 25).All(i => c.W("o_head", i) == 0f) && c.W("o_head", 0) == 100f,
            $"b {st.Eyebrow.LastBlend:0.0000}, [16] {c.W("o_head", 16):0.000}, [0] {c.W("o_head", 0)}");
        for (int i = 0; i < 4; i++) c.Step(F60);   // 0.06 + 4/60 = 0.1267 s
        Check("18.3 fade still running before 0.15 s", st.Eyebrow.IsFading, $"b {st.Eyebrow.LastBlend:0.000}");
        c.Step(F60); c.Step(F60);                    // 0.16 s
        Expect("18.3 b 1 after 0.15 s", c, "o_head", ("g01_warai1", 100), ("e00_defo", 100));

        //
        Neutral(c);
        ex.ChangeEyesPtn(11); st.FinishFades();
        ex.ChangeEyesBlinkFlag(false); ex.ChangeEyesOpenMax(0.6f); c.Step(F60);
        Check("18.4 blink off + open 0.6 → value 0.36 (v²), n 36", st.Eyes.LastN == 36, $"value {st.Eyes.LastValue}, n {st.Eyes.LastN}");
        Expect("18.4 o_head", c, "o_head", ("e01_close", 64), ("e08_odoroki", 36), ("e00_defo", 100));
        Expect("18.4 o_eyelashes", c, "o_eyelashes", ("e01_close", 64), ("e08_odoroki", 36));
        Expect("18.4 o_namida", c, "o_namida", ("e01_close", 64), ("e08_odoroki", 36), ("e00_defo", namidaMouth ? 100 : 0));
        ex.Blink = true; ex.Blink = false; c.Step(F60);
        Expect("18.4 after blink on/off (value 0.6) o_eyelashes", c, "o_eyelashes", ("e01_close", 40), ("e08_odoroki", 60));
        Expect("18.4 after blink on/off o_head", c, "o_head", ("e01_close", 40), ("e08_odoroki", 60), ("e00_defo", 100));

        //
        Neutral(c);
        ex.SetVoicePlaying(true); ex.VoiceLevel = 0.8f;
        st.Width.SclStart = 0.95f; st.Width.SclEnd = 0.92f; st.Width.SegmentDuration = 0.4f; st.Width.SegmentElapsed = 0.2f - F60;
        c.Step(F60);
        Check("18.5 lip sync: FixedRate -1, n 80", st.Mouth.FixedRate == -1f && st.Mouth.LastN == 80, $"fixed {st.Mouth.FixedRate}, n {st.Mouth.LastN}");
        Expect("18.5 o_head", c, "o_head", ("e00_defo", 20), ("k09_open1", 80));
        Expect("18.5 o_tooth", c, "o_tooth", ("k00_defo", 20), ("k02_open1", 80));
        Expect("18.5 o_tang", c, "o_tang", ("k00_defo", 20), ("k02_open1", 80));
        if (namidaMouth) Expect("18.5 o_namida", c, "o_namida", ("e00_defo", 20), ("k09_open1", 80));
        Check("18.5 width 0.935 − 0.2 × 0.8 = 0.775 on cf_J_MouthMove", Mathf.Abs(st.Width.AdjustWidthScale - 0.775f) < 1e-4f && st.MouthWidthBone.localScale.x == st.Width.AdjustWidthScale,
            $"width {st.Width.AdjustWidthScale:0.00000}, bone x {st.MouthWidthBone.localScale.x:0.00000}");
        ex.VoiceLevel = 0.1f; c.Step(F60);
        Check("width 1 while the voice level ≤ 0.2", st.Width.AdjustWidthScale == 1f && st.MouthWidthBone.localScale.x == 1f, $"{st.Width.AdjustWidthScale}");
        ex.VoiceLevel = 1f; c.Step(F60);
        float w1 = st.Width.AdjustWidthScale;
        Check("width at level 1 within [randScaleMin − 0.2, randScaleMax − 0.2]", w1 >= st.Width.RandScaleMin - 0.2f - 1e-5f && w1 <= st.Width.RandScaleMax - 0.2f + 1e-5f, $"{w1:0.0000}");
        ex.SetStudioMouth(9); c.Step(F60);
        Check("mouth 9: jitter off, width kept from the last enabled frame", !st.Width.Enabled && st.Width.AdjustWidthScale == w1 && st.MouthWidthBone.localScale.x == w1, $"{st.Width.AdjustWidthScale:0.0000}");
        ex.SetStudioMouth(0);
        Check("mouth 0: jitter on again", st.Width.Enabled);
        ex.SetVoicePlaying(false);
        c.Step(F60);
        Check("voice stopped: FixedRate back to the slider (0), width 1", st.Mouth.FixedRate == 0f && st.MouthWidthBone.localScale.x == 1f, $"fixed {st.Mouth.FixedRate}, x {st.MouthWidthBone.localScale.x}");
    }

    // ------------------------------------------------------------------ 3. behaviour

    sealed class ScriptedRandom : IFaceRandom
    {
        public readonly Queue<float> Floats = new Queue<float>();
        public readonly Queue<int> Ints = new Queue<int>();
        public float Range(float min, float max) => Floats.Count > 0 ? Floats.Dequeue() : min;
        public int Range(int min, int maxExclusive) => Ints.Count > 0 ? Ints.Dequeue() : min;
    }

    // with the draws of the table: closing 0.17 (count 2), opening 0.18, idle r = 12.
    static void BlinkScripted()
    {
        var rnd = new ScriptedRandom();
        rnd.Floats.Enqueue(0.02f); rnd.Floats.Enqueue(0.03f);
        rnd.Ints.Enqueue(1); rnd.Ints.Enqueue(12);
        var b = new BlinkControl(30, 0.15f, rnd);
        var rate = new Dictionary<int, float>(); var mode = new Dictionary<int, int>(); var count = new Dictionary<int, int>(); var bt = new Dictionary<int, float>();
        int nextClose = -1;
        for (int k = 600; k <= 800; k++)
        {
            int before = b.Mode;
            b.Calc(k / 60f);
            rate[k] = b.OpenRate; mode[k] = b.Mode; count[k] = b.Count; bt[k] = b.BlinkTime;
            if (k > 700 && before == BlinkControl.Idle && b.Mode == BlinkControl.Closing && nextClose < 0) nextClose = k;
        }
        bool Near(float a, float x, float tol = 0.0015f) => Mathf.Abs(a - x) <= tol;
        Check("18.6 10.000 idle → closing, openness 1, blinkTime 10.170, count 2", rate[600] == 1f && mode[600] == 1 && Near(bt[600], 10.17f, 1e-4f) && count[600] == 2, $"{rate[600]} mode {mode[600]} bt {bt[600]:0.0000} count {count[600]}");
        Check("18.6 10.050 → 0.706, 10.150 → 0.118, 10.167 → 0.020", Near(rate[603], 0.706f) && Near(rate[609], 0.118f) && Near(rate[610], 0.020f), $"{rate[603]:0.000} {rate[609]:0.000} {rate[610]:0.000}");
        Check("18.6 10.183 hold 0, count → 1", rate[611] == 0f && mode[611] == 1 && count[611] == 1);
        Check("18.6 10.200 hold 0, closing → opening, blinkTime 10.380", rate[612] == 0f && mode[612] == -1 && Near(bt[612], 10.38f, 1e-4f), $"bt {bt[612]:0.0000}");
        Check("18.6 10.217 → 0.093, 10.300 → 0.556", Near(rate[613], 0.093f) && Near(rate[618], 0.556f), $"{rate[613]:0.000} {rate[618]:0.000}");
        Check("18.6 10.383 opening → idle, openness 1, wait 2.4 s (blinkTime 12.783)", rate[623] == 1f && mode[623] == 0 && Near(bt[623], 12.7833f, 1e-3f), $"bt {bt[623]:0.0000}");
        Check("18.6 next blink at 12.800 (± one frame)", nextClose == 767 || nextClose == 768, $"frame {nextClose} ({nextClose / 60f:0.000} s)");
        var frozen = new BlinkControl(30, 0.15f, new ScriptedRandom());
        frozen.FixedFlags = 1; frozen.Calc(5f);
        Check("fixedFlags ≠ 0 before the first call: no blink starts", frozen.Mode == 0 && frozen.OpenRate == 1f);
        frozen.FixedFlags = 0; frozen.Calc(6f);
        Check("re-enabled: the pending transition happens at once", frozen.Mode == 1);
    }

    // Seeded 60 s run on the character at 1/60: phase durations, hold frames, idle waits, eyebrow sync, eye weights.
    static void BlinkSeeded(Chara c)
    {
        var st = c.St; var ex = c.Ex;
        ex.StartStudio(); st.FinishFades();
        st.SetRandom(new SystemFaceRandom(777));
        st.Blink.ResetFresh();
        c.Step(F60);
        Check("first computation starts a blink (openness 1 in that frame)", st.Blink.Mode == BlinkControl.Closing && st.Eyes.OpenRate == 1f, $"mode {st.Blink.Mode}");
        int frames = 3600, blinks = 0, holdBad = 0, closeBad = 0, openBad = 0, idleBad = 0, syncBad = 0, weightBad = 0;
        float closeStart = st.Clock, closeSpeed = st.Blink.CalcSpeed, openStart = 0, openSpeed = 0, idleStart = 0, idleWait = 0;
        int drawnCount = st.Blink.Count, zeros = 0, prevMode = st.Blink.Mode;
        bool sweepEnded = false;
        double waitSum = 0; int waits = 0; float minCs = 1, maxCs = 0;
        var lash = c.R("o_eyelashes");
        int lashClose = st.Eyes.TargetData(1).Close[0], lashOpen = st.Eyes.TargetData(1).Open[0];   // eyes pattern 0 on this head
        string firstBad = "";
        for (int i = 0; i < frames; i++)
        {
            c.Step(F60);
            var b = st.Blink; float now = st.Clock;
            if (st.Eyebrow.OpenRate != b.OpenRate) syncBad++;
            int n = (int)(float)(b.OpenRate * 100f);
            if (lash.GetBlendShapeWeight(lashOpen) != n || lash.GetBlendShapeWeight(lashClose) != 100 - n) { weightBad++; if (firstBad == "") firstBad = $"t {now:0.000}: rate {b.OpenRate} lash open [{lashOpen}] {lash.GetBlendShapeWeight(lashOpen)}"; }
            if (prevMode == BlinkControl.Closing)
            {
                if (!sweepEnded && now > closeStart + closeSpeed)
                {
                    sweepEnded = true;
                    float d = now - closeStart;
                    if (d < closeSpeed - 1e-4f || d > closeSpeed + F60 + 1e-4f) closeBad++;
                }
                if (b.OpenRate == 0f) zeros++;
            }
            if (b.Mode != prevMode)
            {
                if (b.Mode == BlinkControl.Opening)
                {
                    if (zeros < drawnCount || zeros > drawnCount + 1 || drawnCount < 1 || drawnCount > 3) holdBad++;
                    openStart = now; openSpeed = b.CalcSpeed;
                    minCs = Mathf.Min(minCs, openSpeed); maxCs = Mathf.Max(maxCs, openSpeed);
                }
                else if (b.Mode == BlinkControl.Idle)
                {
                    float d = now - openStart;
                    if (d < openSpeed - 1e-4f || d > openSpeed + F60 + 1e-4f) openBad++;
                    idleStart = now; idleWait = b.BlinkTime - now;
                    float r = idleWait / 0.2f;
                    if (Mathf.Abs(r - Mathf.Round(r)) > 1e-3f || r < -1e-3f || r > 29.001f) idleBad++;
                    waitSum += idleWait; waits++;
                }
                else if (b.Mode == BlinkControl.Closing)
                {
                    float d = now - idleStart;
                    if (d < idleWait - 1e-4f || d > idleWait + F60 + 1e-4f) idleBad++;
                    blinks++;
                    closeStart = now; closeSpeed = b.CalcSpeed; drawnCount = b.Count; zeros = 0; sweepEnded = false;
                    minCs = Mathf.Min(minCs, closeSpeed); maxCs = Mathf.Max(maxCs, closeSpeed);
                }
                prevMode = b.Mode;
            }
        }
        Info($"seeded 60 s: {blinks} blinks, mean idle wait {(waits > 0 ? waitSum / waits : 0):0.00} s, sweep durations {minCs:0.000}..{maxCs:0.000} s");
        Check("seeded run: sweeps within [BaseSpeed, BaseSpeed + 0.05]", minCs >= 0.15f - 1e-5f && maxCs <= 0.20f + 1e-5f && blinks >= 5, $"{minCs:0.000}..{maxCs:0.000}, {blinks} blinks");
        Check("seeded run: closing sweeps last calcSpeed (+ < 1 frame)", closeBad == 0, $"{closeBad} bad");
        Check("seeded run: shut hold = drawn count frames (1..3)", holdBad == 0, $"{holdBad} bad");
        Check("seeded run: opening sweeps last calcSpeed (+ < 1 frame)", openBad == 0, $"{openBad} bad");
        Check("seeded run: idle waits are 0.2 × 0..29 s and are kept (+ < 1 frame)", idleBad == 0, $"{idleBad} bad");
        Check("seeded run: eyebrow openness follows the blink (SyncBlink)", syncBad == 0 && st.SyncBlink, $"{syncBad} frames differ");
        Check("seeded run: eye weights = trunc(openness × 100) every frame", weightBad == 0, weightBad == 0 ? "" : $"{weightBad} frames, first {firstBad}");

        // Blinking off mid-blink: frozen, eyes forced open; on again resumes.
        while (st.Blink.Mode != BlinkControl.Closing) c.Step(F60);
        c.Step(F60);
        var mode = st.Blink.Mode; var bt = st.Blink.BlinkTime;
        ex.Blink = false;
        for (int i = 0; i < 30; i++) c.Step(F60);
        Check("blink off: state frozen, eyes and eyebrow openness 1", st.Blink.Mode == mode && st.Blink.BlinkTime == bt && st.Eyes.OpenRate == 1f && st.Eyebrow.OpenRate == 1f && st.LastBlinkRate == -1f,
            $"mode {st.Blink.Mode}, eyes {st.Eyes.OpenRate}");
        ex.Blink = true; c.Step(F60);
        Check("blink on again: blinking resumes", st.Blink.FixedFlags == 0 && st.LastBlinkRate >= 0f);
    }

    static void Behaviour(Chara c)
    {
        var st = c.St; var ex = c.Ex;
        // Game-side fixed mouth and the Max/Min order quirk.
        Neutral(c);
        ex.ChangeMouthFixed(true); c.Step(F60);
        Check("mouthFixed on: FixedRate = mouthOpenMax (1) → [42] 100", st.Mouth.FixedRate == 1f && c.W("o_head", 42) == 100f && c.W("o_head", 0) == 0f, $"fixed {st.Mouth.FixedRate}");
        ex.ChangeMouthOpenMax(0.5f); ex.ChangeMouthOpenMin(0.2f); c.Step(F60);
        Check("fixed mouth: the last of Max/Min decides FixedRate (Min 0.2)", st.Mouth.FixedRate == 0.2f && st.Mouth.LastN == 20, $"fixed {st.Mouth.FixedRate}, n {st.Mouth.LastN}");
        ex.ChangeMouthFixed(false); c.Step(F60);
        Check("fixed off: FixedRate -1, value OpenMin without voice", st.Mouth.FixedRate == -1f && st.Mouth.LastValue == 0.2f, $"value {st.Mouth.LastValue}");
        ex.ChangeMouthOpenMin(0f); ex.ChangeMouthOpenMax(1f);
        ex.SetMouthOpen(0.37f);
        Check("Studio mouth open → FixedRate 0.37", st.Mouth.FixedRate == 0.37f);
        ex.SetVoicePlaying(true);
        Check("voice + lip sync → FixedRate -1", st.Mouth.FixedRate == -1f);
        ex.SetLipSync(false);
        Check("lip sync off → slider value", st.Mouth.FixedRate == 0.37f);
        ex.SetLipSync(true); ex.SetVoicePlaying(false);
        Check("voice stopped → slider value, level 0", st.Mouth.FixedRate == 0.37f && ex.VoiceLevel == 0f);

        // Eyebrow open max with blinking off forces the eyebrow openness to 1 (quirk 3).
        Neutral(c);
        st.Eyebrow.OpenRate = 0.3f; ex.ChangeEyebrowOpenMax(0.5f);
        Check("eyebrow open max with blink off → eyebrow openness 1", st.Eyebrow.OpenRate == 1f && st.Eyebrow.OpenMax == 0.5f);
        ex.ChangeEyebrowOpenMax(1f);

        // Tongue state and mouth shape mask.
        Neutral(c);
        var headTongue = c.R("o_tang").gameObject;
        var bodyTongues = new List<GameObject>();
        void All(Transform t) { if (t.name == "N_tang") bodyTongues.Add(t.gameObject); for (int i = 0; i < t.childCount; i++) All(t.GetChild(i)); }
        if (c.Ctx.Body) All(c.Ctx.Body.transform);
        Check("pattern 0: head tongue on, body tongue off", headTongue.activeSelf && bodyTongues.All(t => !t.activeSelf), $"body N_tang found {bodyTongues.Count}");
        var low = c.Ctx.HeadBones.TryGetValue("cf_J_MouthLow", out var lowT) ? lowT : null;
        var lowBefore = low ? low.localPosition : Vector3.zero;
        ex.SetStudioMouth(10);
        Check("mouth 10: tongue state 1 (head tongue off, body tongue on), mask on, jitter off",
            ex.Status.TongueState == 1 && !headTongue.activeSelf && bodyTongues.All(t => t.activeSelf) && ex.Status.DisableMouthShapeMask && !st.Width.Enabled);
        var lowMasked = low ? low.localPosition : Vector3.zero;
        ex.SetStudioMouth(17);
        Check("mouth 17: tongue 0, mask on, jitter on", ex.Status.TongueState == 0 && headTongue.activeSelf && ex.Status.DisableMouthShapeMask && st.Width.Enabled);
        ex.SetStudioMouth(13);
        Check("mouth 13: tongue 1, mask on", ex.Status.TongueState == 1 && ex.Status.DisableMouthShapeMask);
        ex.SetStudioMouth(0);
        var lowOff1 = low ? low.localPosition : Vector3.zero;
        ex.SetStudioMouth(18); ex.SetStudioMouth(0);
        var lowOff2 = low ? low.localPosition : Vector3.zero;
        Check("mouth 0: mask off, tongue 0; mask on/off cycles are repeatable", !ex.Status.DisableMouthShapeMask && headTongue.activeSelf && lowOff1 == lowOff2,
            $"cf_J_MouthLow at build {lowBefore.ToString("F5")}, masked {lowMasked.ToString("F5")}, unmasked {lowOff1.ToString("F5")}");
        ex.ChangeMouthPtn(13);
        Check("game ChangeMouthPtn(13) leaves the mask alone", !ex.Status.DisableMouthShapeMask && ex.Status.TongueState == 1);
        ex.SetStudioMouth(0);
    }

    static void Materials(Chara c, ContentCatalog gd, string path)
    {
        var ex = c.Ex; var m = ex.Materials;
        Neutral(c);
        Check("materials found (o_namida, face, 2 eyes)", m.HasTears && m.FaceMaterial && m.EyeCount == 2);
        Check("start values: tears 0, cheek 0, highlight shown (_Smoothness 1)",
            m.TearsMaterial.GetFloat(FaceMaterials.TearsProperty) == 0f && m.FaceMaterial.GetFloat(FaceMaterials.CheekProperty) == 0f
            && m.EyeMaterial(0).GetFloat(FaceMaterials.HighlightProperty) == 1f && m.EyeMaterial(1).GetFloat(FaceMaterials.HighlightProperty) == 1f);
        ex.Tears = 0.5f;
        Check("tears 0.5 → _NamidaScale 0.5", m.TearsMaterial.GetFloat(FaceMaterials.TearsProperty) == 0.5f);
        ex.Tears = 2f;
        Check("tears 2 → clamped 1", m.TearsMaterial.GetFloat(FaceMaterials.TearsProperty) == 1f && ex.Tears == 1f);
        ex.Tears = -1f;
        Check("tears -1 → clamped 0", m.TearsMaterial.GetFloat(FaceMaterials.TearsProperty) == 0f);
        ex.Cheek = 0.7f;
        Check("cheek 0.7 → _Texture4Scale 0.7 on the face draw material (o_head slot 0 = ctx.FaceDraw)",
            m.FaceMaterial.GetFloat(FaceMaterials.CheekProperty) == 0.7f && m.FaceMaterial == c.Ctx.FaceDraw);
        ex.HideEyeHighlight(true);
        Check("highlight hidden → _Smoothness 0 on both eyes", m.EyeMaterial(0).GetFloat(FaceMaterials.HighlightProperty) == 0f && m.EyeMaterial(1).GetFloat(FaceMaterials.HighlightProperty) == 0f);
        ex.HideEyeHighlight(false);
        Check("highlight shown → _Smoothness 1", m.EyeMaterial(0).GetFloat(FaceMaterials.HighlightProperty) == 1f);
        // Per-character instances: a second build of the same card has its own materials.
        var c2 = Build(gd, path, "female copy");
        c2.Ex.Tears = 0.25f; c2.Ex.Cheek = 0.1f;
        ex.Tears = 0.75f;
        Check("materials are per character (second build independent)",
            c2.Ex.Materials.TearsMaterial != m.TearsMaterial && c2.Ex.Materials.TearsMaterial.GetFloat(FaceMaterials.TearsProperty) == 0.25f
            && m.TearsMaterial.GetFloat(FaceMaterials.TearsProperty) == 0.75f && c2.Ex.Materials.FaceMaterial != m.FaceMaterial
            && c2.Ex.Materials.EyeMaterial(0) != m.EyeMaterial(0));
        Object.DestroyImmediate(c2.Go);
        ex.Tears = 0f; ex.Cheek = 0f;
    }

    static void UiMapping(Chara c)
    {
        var ex = c.Ex; var st = c.St; string L = c.Label;
        Neutral(c);
        Check($"{L} lists = spec tables (source {ex.Lists.Source})",
            ex.Lists.Eyebrow.SequenceEqual(ExpressionLists.DefaultEyebrow) && ex.Lists.Eyes.SequenceEqual(ExpressionLists.DefaultEyes) && ex.Lists.Mouth.SequenceEqual(ExpressionLists.DefaultMouth),
            $"eyebrow {string.Join(",", ex.Lists.Eyebrow)}; eyes {string.Join(",", ex.Lists.Eyes)}; mouth {string.Join(",", ex.Lists.Mouth)}");
        bool ok = true; string got = "";
        ex.ChangeEyebrowPtn(15, false);   // so that every step below is a change (and fades)
        for (int p = 0; p < ex.EyebrowUiCount; p++)
        {
            ex.SetEyebrowUiIndex(p);
            got += $" {p}→{ex.EyebrowPtn}";
            ok &= ex.EyebrowPtn == ExpressionLists.DefaultEyebrow[p] && ex.EyebrowUiIndex == p && st.Eyebrow.CurrentPattern == ex.EyebrowPtn && st.Eyebrow.IsFading;
        }
        Check($"{L} eyebrow UI position → id (blend true)", ok, got.Trim());
        ok = true; got = "";
        for (int p = 0; p < ex.EyesUiCount; p++)
        {
            ex.SetEyesUiIndex(p);
            got += $" {p}→{ex.EyesPtn}";
            ok &= ex.EyesPtn == ExpressionLists.DefaultEyes[p] && ex.EyesUiIndex == p && st.Eyes.CurrentPattern == ex.EyesPtn;
        }
        Check($"{L} eyes UI position → id", ok, got.Trim());
        ex.ChangeEyebrowPtn(4); ex.ChangeEyesPtn(10);
        Check($"{L} unlisted ids (eyebrow 4, eyes 10) show position 0", ex.EyebrowUiIndex == 0 && ex.EyesUiIndex == 0 && ex.EyebrowPtn == 4 && ex.EyesPtn == 10);
        ex.SetEyebrowUiIndex(0); ex.StepEyebrowUi(-1);
        bool w1 = ex.EyebrowUiIndex == 9 && ex.EyebrowPtn == 15;
        ex.StepEyebrowUi(1);
        bool w2 = ex.EyebrowUiIndex == 0 && ex.EyebrowPtn == 0;
        ex.SetEyesUiIndex(0); ex.StepEyesUi(-1);
        bool w3 = ex.EyesUiIndex == 8 && ex.EyesPtn == 13;
        ex.StepEyesUi(1);
        bool w4 = ex.EyesUiIndex == 0;
        Check($"{L} ◀ at 0 wraps to the last entry, ▶ wraps to 0 (eyebrow, eyes)", w1 && w2 && w3 && w4);
        ex.SetStudioMouth(25); ex.StepMouthUi(1);
        bool m1 = ex.MouthUiIndex == 0 && !st.Mouth.IsFading;
        ex.StepMouthUi(-1);
        bool m2 = ex.MouthUiIndex == 25 && ex.MouthPtn == 25 && st.Mouth.CurrentPattern == 25;
        Check($"{L} mouth UI = raw id 0..{ex.MouthUiCount - 1} with wrap, blend false", m1 && m2 && ex.MouthUiCount == st.Mouth.PatternCount, $"count {ex.MouthUiCount}");
        ex.SetStudioMouth(0);
    }

    static void Rejection(Chara c)
    {
        var ex = c.Ex; var st = c.St;
        Neutral(c);
        ex.ChangeEyebrowPtn(2, false); ex.ChangeEyesPtn(1, false); ex.SetStudioMouth(3);
        int r0 = ex.Rejections;
        bool all = !ex.ChangeEyebrowPtn(16) && !ex.ChangeEyebrowPtn(-1) && !ex.ChangeEyesPtn(16) && !ex.ChangeEyesPtn(-5)
                   && !ex.ChangeMouthPtn(26) && !ex.ChangeMouthPtn(-3) && !ex.SetStudioMouth(99)
                   && !ex.SetEyebrowUiIndex(10) && !ex.SetEyesUiIndex(-1);
        Check("out-of-range ids rejected (eyebrow 16/-1, eyes 16/-5, mouth 26/-3/99, UI 10/-1)", all && ex.Rejections - r0 == 9, $"{ex.Rejections - r0} rejections");
        Check("rejection keeps status and face", ex.EyebrowPtn == 2 && ex.EyesPtn == 1 && ex.MouthPtn == 3 && st.Eyebrow.CurrentPattern == 2 && st.Eyes.CurrentPattern == 1 && st.Mouth.CurrentPattern == 3 && ex.Status.TongueState == 0);
        var bad = new FaceStatus { EyebrowPtn = 4, EyesPtn = 3, MouthPtn = 99 };
        ex.ApplyStudioState(bad);
        Check("Studio state: unlisted eyebrow 4 / eyes 3 → 0; invalid mouth 99 keeps the shown pattern in the status",
            ex.EyebrowPtn == 0 && ex.EyesPtn == 0 && ex.MouthPtn == st.Mouth.CurrentPattern && st.Mouth.CurrentPattern == 3, $"mouth status {ex.MouthPtn}, shown {st.Mouth.CurrentPattern}");
        var r1 = st.Mouth.ChangeFace(new[] { 1, 2 }, new[] { 1f, 0.5f }, 2, false);
        var r2 = st.Mouth.ChangeFace(new[] { 30 }, new[] { 1f }, 1, false);
        var r3 = st.Mouth.ChangeFace(new[] { 1, 2 }, new[] { 0.5f, 0.5f }, 2, false);
        c.Step(F60);
        Check("ChangeFace: weight sum 1.5 rejected (2), key 30 rejected (3), mix {1: .5, 2: .5} accepted",
            r1 == FbsControl.ChangeResult.WeightSum && r2 == FbsControl.ChangeResult.BadPattern && r3 == FbsControl.ChangeResult.Ok
            && c.W("o_head", 26) == 50f && c.W("o_head", 28) == 50f, $"{r1}, {r2}, {r3}; [26] {c.W("o_head", 26)} [28] {c.W("o_head", 28)}");
        Neutral(c);
    }

    static float[] Snapshot(Chara c)
    {
        var l = new List<float>();
        foreach (var r in new[] { "o_head", "o_eyelashes", "o_namida", "o_tooth", "o_tang" })
        {
            var smr = c.R(r);
            for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++) l.Add(smr.GetBlendShapeWeight(i));
        }
        var b = c.St.Blink;
        l.Add(b.Mode); l.Add(b.BlinkTime); l.Add(b.OpenRate); l.Add(b.Count); l.Add(c.St.Clock); l.Add(c.St.Eyebrow.FadeProgress);
        l.Add(c.St.Width.SegmentElapsed); l.Add(c.St.MouthWidthBone.localScale.x);
        return l.ToArray();
    }

    static void HoldAndReset(Chara c)
    {
        var ex = c.Ex; var st = c.St;
        Neutral(c);
        ex.Blink = true;
        st.Blink.ResetFresh();
        ex.ChangeEyebrowPtn(2);
        ex.SetVoicePlaying(true); ex.VoiceLevel = 0.7f;
        for (int i = 0; i < 4; i++) c.Step(F60);   // mid fade, mid closing sweep
        var a = Snapshot(c);
        for (int i = 0; i < 5; i++) c.Step(0f);
        var b = Snapshot(c);
        int diff = 0; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) diff++;
        Check("Step(0) holds weights, blink, fade, width (5 calls)", diff == 0 && st.Blink.Mode == BlinkControl.Closing && st.Eyebrow.IsFading, $"{diff} of {a.Length} values changed, fade {st.Eyebrow.FadeProgress:0.00}");
        ex.SetVoicePlaying(false);
        ex.ChangeEyebrowPtn(1);
        c.Step(F60);
        bool fading = st.Eyebrow.IsFading;
        c.Ctx.Motion.ResetAll();
        c.Step(F60);
        Check("ResetAll: cross-fade finished, eyes open, idle", fading && !st.Eyebrow.IsFading && c.W("o_head", 16) == 100f && c.W("o_head", 17) == 0f
            && st.Eyes.OpenRate == 1f && st.Blink.Mode != BlinkControl.Opening, $"[16] {c.W("o_head", 16)}, eyes {st.Eyes.OpenRate}, mode {st.Blink.Mode}");

        // Allocation probe (informational): the stage alone, 2000 steps.
        long m0 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
        var frame = new MotionFrame(F60, F60, 0f, 0);
        for (int i = 0; i < 2000; i++) st.Step(frame);
        long m1 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
        Info($"mono heap delta over 2000 stage steps: {m1 - m0} bytes (informational)");
        Neutral(c);
    }

    sealed class FakeLook : IEyeLookOutputs
    {
        public float H, V;
        public bool HasOutput => true;
        public float AngleH(int eye) => 0f;
        public float AngleV(int eye) => 0f;
        public float AngleHRate(int eye) => H;
        public float AngleVRate => V;
    }

    static void Correction(Chara c)
    {
        var st = c.St; var ex = c.Ex;
        Neutral(c);
        ex.Blink = true;
        c.Step(F60);
        Check("eyelid correction off by default: correctOpenMax -1", !ex.EyelidCorrection && st.Eyes.CorrectOpenMax == -1f);
        float u0 = st.UpCorrect, d0 = st.DownCorrect, s0 = st.SideCorrect;
        Info($"head constants {u0}/{d0}/{s0}: straight ahead {st.Correction(0, 0):0.000}, up {st.Correction(0, 1):0.000}, down {st.Correction(0, -1):0.000}, side {st.Correction(1, 0):0.000}");
        st.UpCorrect = 0.1f; st.DownCorrect = 0.3f; st.SideCorrect = 0.1f;   // the examples use the vanilla constants
        float Cor(float h, float v) => st.Correction(h, v);
        Check("examples: ahead 0.9, up 1.0, down 0.6, side 0.8, down+side 0.6",
            Mathf.Abs(Cor(0, 0) - 0.9f) < 1e-5f && Mathf.Abs(Cor(0, 1) - 1f) < 1e-5f && Mathf.Abs(Cor(0, -1) - 0.6f) < 1e-5f
            && Mathf.Abs(Cor(1, 0) - 0.8f) < 1e-5f && Mathf.Abs(Cor(1, -1) - 0.6f) < 1e-5f,
            $"{Cor(0, 0):0.000} {Cor(0, 1):0.000} {Cor(0, -1):0.000} {Cor(1, 0):0.000} {Cor(1, -1):0.000}");
        var old = st.EyeLook;
        var look = new FakeLook { H = 0f, V = -1f };
        st.EyeLook = look; ex.EyelidCorrection = true;
        c.Step(F60);
        Check("correction on with an eye-look source (down, constants 0.1/0.3/0.1): correctOpenMax 0.6", Mathf.Abs(st.Eyes.CorrectOpenMax - 0.6f) < 1e-5f, $"{st.Eyes.CorrectOpenMax:0.0000}");
        ex.Blink = false; look.V = 1f; c.Step(F60);
        Check("blinking off: correction kept stale (game behaviour)", Mathf.Abs(st.Eyes.CorrectOpenMax - 0.6f) < 1e-5f);
        st.ResetCorrectionWhenFixed = true; c.Step(F60);
        Check("ResetCorrectionWhenFixed: dropped to -1", st.Eyes.CorrectOpenMax == -1f);
        st.ResetCorrectionWhenFixed = false; ex.EyelidCorrection = false; st.EyeLook = old;
        st.UpCorrect = u0; st.DownCorrect = d0; st.SideCorrect = s0;
        c.Step(F60);
        Check("correction off again: -1", st.Eyes.CorrectOpenMax == -1f);
        Info($"eye-look stage found at attach: {(old != null ? old.GetType().Name : "none")}");
        Neutral(c);
    }

    // ------------------------------------------------------------------ 4. renders

    // Without a player loop (batch, edit mode) the skinned meshes are not re-skinned after their first render, so blend-shape
    // and bone changes would not show in later Camera.Render calls. Toggling the renderer marks it for a fresh skin.
    static void ForceSkinning(GameObject root)
    {
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            smr.forceMatrixRecalculationPerRender = true;
            bool e = smr.enabled;
            smr.enabled = false; smr.enabled = e;
        }
    }

    static void Render(Chara c)
    {
        var ex = c.Ex; var st = c.St;
        var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HS2Next.App.AppPipeline.MeasureAssetPath);
        int swapped = HS2Next.ShaderMap.ApplyUrp(c.Go);
        ex.RefreshMaterials();
        Info($"URP: swapped {swapped} materials");
        var cases = new List<(string name, Action set)>
        {
            ("eyebrow0", () => ex.ChangeEyebrowPtn(0)), ("eyebrow1", () => ex.ChangeEyebrowPtn(1)), ("eyebrow2", () => ex.ChangeEyebrowPtn(2)),
            ("eyes0", () => ex.ChangeEyesPtn(0)), ("eyes1", () => ex.ChangeEyesPtn(1)), ("eyes2", () => ex.ChangeEyesPtn(2)),
            ("mouth0", () => ex.SetStudioMouth(0)), ("mouth1", () => ex.SetStudioMouth(1)), ("mouth2", () => ex.SetStudioMouth(2)), ("mouth3", () => ex.SetStudioMouth(3)),
            ("tears1", () => ex.Tears = 0.5f), ("tears2", () => ex.Tears = 1f),
            ("eyesopen03_studio", () => ex.EyesOpen = 0.3f),                                   // blink off: shows 0.3² = 0.09
            ("eyesopen03", () => { ex.EyesOpen = 0.3f; ex.Blink = true; ex.Blink = false; }), // openness 1: shows 0.3
        };
        foreach (var (name, set) in cases)
        {
            Neutral(c);
            set();
            st.FinishFades();
            c.Step(F60);
            ForceSkinning(c.Go);
            string prefix = Path.Combine(OutDir, "face_" + name);
            CharaRenderTest.RenderViews(c.Go, prefix, asset, "face");
            string src = prefix + "_face.png", dst = prefix + ".png";
            if (File.Exists(dst)) File.Delete(dst);
            bool ok = File.Exists(src);
            if (ok) File.Move(src, dst);
            Check($"render face_{name}.png", ok && new FileInfo(dst).Length > 1000,
                $"eyebrow {st.Eyebrow.CurrentPattern} eyes {st.Eyes.CurrentPattern} (n {st.Eyes.LastN}) mouth {st.Mouth.CurrentPattern} (n {st.Mouth.LastN}) tears {ex.Materials.TearsMaterial?.GetFloat(FaceMaterials.TearsProperty)}");
        }
        GraphicsSettings.defaultRenderPipeline = null; QualitySettings.renderPipeline = null;
    }
}
