// Batch comparison against HS2 itself ("HS2 実機との比較").
// Input: Cache/compare/<card>/<case>.json (+ PNG captures), recorded in a running CharaStudio by Tools/hs2_compare.py
// (format in Cache/compare/README.md). This test only reads those files; HS2Next never talks to the game.
// Run: -executeMethod CompareTest.Run [-card <name>] [-render] [-cases rest,pose_3,...]
//   -card    only the card whose folder name or card file name contains <name> (case-insensitive)
//   -render  (graphics device) also render the visual cases with the recorded camera and lights in URP and write
//            side-by-side images (HS2 left, HS2Next right, same size) and per-pixel difference images
//   every case builds the card through the documents: imported, saved, reloaded (DocumentRoundTrip) and built from them
//            (pose cases: the importer registered, so SceneOps.AddCharaFromCard imports). The Phase 1 card path is retired;
//            -source card / both are accepted and read as document (one line in the report says so).
//   -cases   only these cases (names as below, pose_<n>)
//   -sliderRange clamp|extrapolate  ShapeEngine.SliderRange for the run (default: extrapolate, the reference install's slider
//            extender). Cards recorded with --card-files (v0.2: cards whose shape values lie outside 0..1) have only the
//            rest case and no captures.
// Pose cases (Phase 2.3; recorded by Tools/hs2_compare.py --cases pose from
// Tools/compare_poses.json): through the scene path (RuntimeServices with the extension modules whose HS2 controllers
// the recording switched off disabled, at least card ABMX; FkOptions.HipsOffset on; HS2US categories on), a session
// and runtime without drivers, SceneOps.AddCharaFromCard, physics stages off, T-pose at speed 0, eye pattern 0, blink
// off, PoseImport.ApplyFile, Tick(1/60) until still (min 20, max 600). Compared: the FK bones of the applied groups
// (report.AppliedBones, partners, nodes present) ≤ 0.1°, cf_J_Hips local position ≤ 1e-3, every other skeleton
// transform with the anime thresholds, face ids and opens, correction, fk/ik flags and the seven groups (equal),
// HS2's fk.list rot / pos = the file = the HS2Next model (float ==), and the report (unresolved keys only of the AI and
// node families; the anime record resolved). A case the recording marks invalid (cf_N_height moved) is reported, not
// counted.
// For each recorded case the same card is built (CharaAssembler), the case is reproduced with the same switches, the
// motion stages are stepped by hand (60 Hz) until the bones are still, and compared:
//   rest_plugins  T-pose 0/0/0 at speed 0, physics off, look off (HS2Next: neck pattern 3 = animation, eyes 0), blink off,
//                 card plugin data on
//   rest          the same with the ABMX stage off (Studio: ABMX modifiers removed, plugin controllers off)
//   anime_idle / anime_chair  19/0/0 and 19/1/11 at normalized time 0.5, speed 0, physics off, ABMX off
//   look          T-pose, eye pattern 4 with the look-at target at the recorded local position under cf_J_Head, neck
//                 pattern 1 with the camera at the recorded world position (the character stands at the origin facing +z
//                 in both programs)
//   face          eyebrow / eyes UI 1, mouth 3, mouth open 0.5, blink off, tears 0.5 (recorded on a fresh character set up
//                 as for rest, so it depends on no other case; until 2.4 it followed the look case on the same character
//                 and the game's eyes stayed part-way back from the look target, a known deviation of cf_J_look_L/R)
// Bones: every transform of the HS2 dump (bones.dump root all) found on the HS2Next character by path (body paths from
// cf_J_Root, head paths from p_cf_head_bone): local position (max component), rotation (quaternion angle), scale (max
// component); max, mean and the 15 worst. PASS thresholds per case (on the skeleton group, names cf_*/cm_*/N_*/k_f_*):
//   rest, rest_plugins, face: position 1e-3, angle 0.1 deg, scale 1e-3. Phase 1.7 reproduced the shape stage to < 1e-4 /
//     0.05 deg; the margin covers float differences of the T-pose sampling and of the joint corrections.
//   anime_*: position 1e-3, angle 0.25 deg, scale 1e-3: the clip is sampled by a different engine (PlayableGraph vs the
//     game's Animator, measured ≤ 5e-6 per quaternion component on the spec examples) and the corrections run on top.
//   look: angle 1 deg: neck and eye smoothing are frame-rate dependent (lerp(dt * speed)) and Studio ran at its own frame
//     rate; both have converged after 5 s, but the head-space look target was recorded 1-2 frames after the neck started.
// Game-side plugin state reproduced on the HS2Next character (the reference install runs Studio with plugins; the
// "plugins off" cases switch the card plugin controllers off, but what other plugins did when the character was added
// stays):
//   - joint correction: HS2US "Auto Joint Correction" (default All) switches all 8 categories on when Studio adds a
//     character (vanilla: all off); every recorded case has them on, so the build uses
//     CorrectSetup.InitialCategoriesOn.
//   - eye look: the EyeLookAdjuster plugin rewrites the EyeLookCalc type states at load (its own config, not card
//     data); the recorded states (face.eyeLook.eyeLookScript.eyeTypeStates) are copied into the HS2Next eye stage,
//     and the fields that differ from the bundle data are listed in the report.
// Known deviations (KnownDeviations below): transforms whose game value is not produced by anything HS2Next
// implements; they are listed with the reason and left out of the PASS / FAIL decision.
// Blend shapes: per renderer under the head and per shape name, the weights must be equal (they are integers 0..100).
// Look: the eye stage's angleH / angleV and the neck stage's per-bone angles against the game's EyeLookCalc / NeckLookCalcVer2.
// Report: Spike/out/compare/compare_report.txt.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Chara;
using HS2Next.Chara.Documents;
using HS2Next.Chara.Ext;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Data;
using HS2Next.Motion;
using HS2Next.Motion.Animation;
using HS2Next.Motion.Corrective;
using HS2Next.Motion.DynamicBones;
using HS2Next.Motion.Face;
using HS2Next.Motion.Fk;
using HS2Next.Motion.Look;
using HS2Next.Scenes.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class CompareTest
{
    const float Dt = 1f / 60f;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static string DataDir => Path.Combine(ContentSettings.ProjectRoot, "Cache", "compare");
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "compare");

    static string Arg(string name, string def)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    static StringBuilder rep;
    static readonly List<string> table = new List<string>();
    static readonly List<string> summary = new List<string>();
    static int passes, fails;
    static StudioDb db;
    static string[] sources = { "document" };
    static HashSet<string> onlyCases;
    static readonly Dictionary<string, string> boneMetrics = new Dictionary<string, string>();

    public static void Run()
    {
        rep = new StringBuilder(); table.Clear(); summary.Clear(); passes = fails = 0; boneMetrics.Clear(); poseMetrics.Clear();
        Directory.CreateDirectory(OutDir);
        bool render = TestArgs.Get().Contains("-render");
        string only = Arg("-card", null);
        string source = Arg("-source", "document");
        sources = new[] { "document" };
        var cs0 = Arg("-cases", null);
        onlyCases = cs0 == null ? null : new HashSet<string>(cs0.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0));
        var head = new StringBuilder();
        var gd = CardResolveTest.BuildDb(head);
        db = gd.LoadStudioLists();
        rep.AppendLine($"HS2 comparison (Editor/CompareTest.cs), {DateTime.Now:yyyy-MM-dd HH:mm}, Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}, render {(render ? "on" : "off")}, source {string.Join(" + ", sources)}");
        rep.AppendLine("data: Cache/compare (Tools/hs2_compare.py); " + head.ToString().Trim());
        int? seed = FaceSetup.Seed;
        var oldCam = LookTargets.DefaultCamera;
        bool oldCorr = CorrectSetup.InitialCategoriesOn;
        var oldRange = ShapeEngine.SliderRange;
        ShapeEngine.SliderRange = Arg("-sliderRange", "extrapolate") == "clamp" ? ShapeEngine.Range.Clamp : ShapeEngine.Range.Extrapolate;
        rep.AppendLine($"slider range: {ShapeEngine.SliderRange}");
        if (source != "document") rep.AppendLine($"-source {source}: the card path is retired; every case builds through the documents");
        try
        {
            FaceSetup.Seed = 1234;
            CorrectSetup.InitialCategoriesOn = true;   // HS2US "Auto Joint Correction" in the recorded Studio (see header)
            if (!Directory.Exists(DataDir)) throw new DirectoryNotFoundException(DataDir + " (run Tools/hs2_compare.py first)");
            foreach (var dir in Directory.GetDirectories(DataDir).OrderBy(d => d, StringComparer.Ordinal))
            {
                // a card folder is one whose meta.json names a card; the other recordings next to them (effects, maps, items,
                // accessory items, ...) are compared elsewhere - each skipped folder is named, so a broken card meta shows
                string metaPath = Path.Combine(dir, "meta.json");
                var meta = File.Exists(metaPath) ? Json.Parse(File.ReadAllText(metaPath)) as Dictionary<string, object> : null;
                string card = meta != null ? S(meta, "card") : null, sex = meta != null ? S(meta, "sex") : null;
                if (string.IsNullOrEmpty(card)) { rep.AppendLine("skipped (not a card folder): " + Path.GetFileName(dir)); continue; }
                if (only != null && Path.GetFileName(dir).IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0 && card.IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0) continue;
                Guard("card " + card, () => RunCard(gd, dir, card, sex, render));
            }
        }
        catch (Exception e) { Fail("run", e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        finally
        {
            FaceSetup.Seed = seed;
            CorrectSetup.InitialCategoriesOn = oldCorr;
            ShapeEngine.SliderRange = oldRange;
            LookTargets.DefaultCamera = oldCam;
            GraphicsSettings.defaultRenderPipeline = null; QualitySettings.renderPipeline = null;
            gd.UnloadAll();
        }
        var all = new StringBuilder();
        all.AppendLine($"SUMMARY: {passes} PASS, {fails} FAIL");
        foreach (var s in summary) all.AppendLine("  " + s);
        if (table.Count > 0)
        {
            all.AppendLine();
            all.AppendLine("visual: mean |HS2 - HS2Next| per pixel and channel (sRGB 0..255): whole image / foreground (pixels not background in either image)");
            all.AppendLine($"  {"card",-18} {"case",-12} {"view",-5} {"vs HS2 no effects",-24} {"vs HS2 with effects",-24}");
            foreach (var t in table) all.AppendLine("  " + t);
        }
        all.AppendLine();
        all.Append(rep);
        File.WriteAllText(Path.Combine(OutDir, "compare_report.txt"), all.ToString());
        Debug.Log($"COMPARE TEST DONE: {passes} PASS, {fails} FAIL");
    }

    static void Guard(string what, Action a)
    {
        try { a(); }
        catch (Exception e) { Fail(what + " (exception)", e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
    }

    static void Check(string name, bool ok, string detail)
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
        summary.Add($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
        if (ok) passes++; else fails++;
    }
    static void Fail(string name, string detail) => Check(name, false, detail);

    // ================================================================ per card

    static void RunCard(ContentCatalog gd, string dir, string card, string sex, bool render)
    {
        string path = Path.Combine(CardResolveTest.GameRoot, "UserData", "chara", sex == "male" ? "male" : "female", card.Replace('/', Path.DirectorySeparatorChar));
        string cardName = Path.GetFileName(dir);
        rep.AppendLine();
        rep.AppendLine($"==== {card} ({sex}) -> {cardName}");
        if (!File.Exists(path)) { Fail(cardName + ": card file", path); return; }
        foreach (var c in new[] { "rest_plugins", "rest", "anime_idle", "anime_chair", "look", "face" })
        {
            if (onlyCases != null && !onlyCases.Contains(c)) continue;
            string f = Path.Combine(dir, c + ".json");
            if (!File.Exists(f)) { rep.AppendLine($"-- {c}: no data"); continue; }
            var data = (Dictionary<string, object>)Json.Parse(File.ReadAllText(f));
            foreach (var src in sources)
                Guard($"{cardName} {c}{Tag(src)}", () => RunCase(gd, path, dir, cardName, sex, c, data, render && src == sources[0], src));
            if (sources.Length > 1)
            {
                boneMetrics.TryGetValue($"{cardName}|{c}|card", out var mc);
                boneMetrics.TryGetValue($"{cardName}|{c}|document", out var md);
                Check($"{cardName} {c}: metrics equal for the card and the document build (max / mean per group)", mc != null && mc == md, mc == md ? "" : $"card {mc} / document {md}");
            }
        }
        RunPoseCases(gd, dir, card, sex, sources.Length > 1 ? "both" : sources[0]);
        if (onlyCases == null || onlyCases.Contains("rest_identity")) Guard($"{cardName} rest_identity", () => RunRestIdentity(gd, dir, path, cardName, sex));
    }

    // D6: the rest case through the scene path with FkOptions.ResetToIdentity
    // (vanilla: a new Studio character's Hair / Body / Skirt FK bones are set to identity). Checks that this explains the
    // known deviation cf_J_sk_04_01/02 (the game holds 0, the prefab -12 / -14 deg).
    static void RunRestIdentity(ContentCatalog gd, string dir, string cardPath, string cardName, string sex)
    {
        string f = Path.Combine(dir, "rest.json");
        if (!File.Exists(f)) return;
        var data = (Dictionary<string, object>)Json.Parse(File.ReadAllText(f));
        rep.AppendLine("-- rest_identity: the rest case through the scene path with FkOptions.ResetToIdentity (D6)");
        var services = new RuntimeServices { Catalog = gd, Db = db, Hs2Root = CardResolveTest.GameRoot, Settings = new SceneRuntimeSettings { ManualStepping = true } };
        services.ExtOptions = new CharaExtOptions { Abmx = false };
        bool oldId = FkOptions.ResetToIdentity;
        var oldFk = CharaPoseDrivers.Fk;
        SceneRuntime rt = null;
        try
        {
            FkOptions.ResetToIdentity = true;
            if (!(CharaPoseDrivers.Fk is FkDriver)) CharaPoseDrivers.Fk = FkDriver.Instance;
            var s = SceneRuntimeTest.NewSession(services);
            rt = new SceneRuntime(s, services, null, false);
            var add = rt.Ops.AddCharaFromCard(cardPath);
            if (add == null || !add.Ok) { Fail($"{cardName} rest_identity add", add?.ToString()); return; }
            string id = add.NewId;
            s.SetAnimation(id, new HS2Next.Save.AnimeRef(ContentIds.Vanilla, 0, 0, 0));
            s.Set(id, "chara.anime.speed", 0f);
            s.Set(id, "chara.look.eyes", 0);
            s.Set(id, "chara.look.neck", 3);
            rt.ApplyNow();
            var r = rt.FindChara(id);
            var dyn = r.Motion.Get<DynamicBoneStage>(); if (dyn != null) dyn.Enabled = false;
            var bust = r.Motion.Get<BustStage>(); if (bust != null) bust.Enabled = false;
            var built = new Built { Root = r.Root, Ctx = r.Context, M = r.Motion, A = r.Animation, Look = r.Look, Expr = r.Expression, Bust = bust };
            built.BodyRoot = CharaAssembler.Find(r.Context.Anim.transform, "cf_J_Root");
            built.HeadRoot = r.Context.HeadBone ? r.Context.HeadBone.transform : null;
            var recs = new List<Rec>();
            foreach (var o in L(D(data, "dump"), "bones"))
            {
                var e = (Dictionary<string, object>)o;
                var x = new Rec { Name = S(e, "name"), Path = S(e, "path"), P = V3(e["lp"]), Q = Q4(e["lq"]), S = V3(e["ls"]) };
                x.T = Resolve(built, x.Path, x.Name);
                recs.Add(x);
            }
            SettleScene(rt, built, recs.Where(x => x.T).Select(x => x.T).ToList(), 20, 600);
            var sk = recs.Where(x => x.T && (x.Name == "cf_J_sk_04_01" || x.Name == "cf_J_sk_04_02")).Select(x => (x, da: Angle(x.T.localRotation, x.Q))).ToList();
            Check($"{cardName} rest_identity: cf_J_sk_04_01/02 match the game with the vanilla FK initialisation (D6, ≤ 0.1°)", sk.Count == 2 && sk.All(x => x.da <= 0.1f),
                  string.Join("; ", sk.Select(x => $"{x.x.Name} {x.da:G3}° (HS2 le {Fmt(x.x.Q.eulerAngles)}, HS2Next le {Fmt(x.x.T.localRotation.eulerAngles)})")));
            var rows = recs.Where(x => x.T && Skeleton(x)).Select(x => (x, da: Angle(x.T.localRotation, x.Q))).Where(x => x.da >= 0.1f).OrderByDescending(x => x.da).ToList();
            rep.AppendLine($"   skeleton transforms over 0.1° with ResetToIdentity: {rows.Count}" + (rows.Count > 0 ? " (" + string.Join(", ", rows.Take(10).Select(x => $"{x.x.Name} {x.da:G3}")) + ")" : ""));
        }
        finally
        {
            rt?.Dispose();
            FkOptions.ResetToIdentity = oldId;
            CharaPoseDrivers.Fk = oldFk;
        }
    }

    static string Tag(string src) => sources.Length > 1 ? " (" + src + ")" : (src == "document" ? " (document)" : "");

    sealed class Built
    {
        public GameObject Root; public CharaContext Ctx; public CharaAssembler Asm; public CharaMotion M;
        public CharaAnimation A; public CharaLook Look; public CharaExpression Expr; public BustStage Bust;
        public Transform BodyRoot, HeadRoot;
    }

    static Built Build(ContentCatalog gd, string path, bool plugins, string source = "document")
    {
        var asm = new CharaAssembler(gd);
        asm.ExtOptions.Abmx = plugins;       // Studio "rest": ABMX modifiers removed, plugin controllers off
        // A4: the card imported, saved and reloaded, then built from the documents (package C's DocumentRoundTrip)
        var rt = DocumentRoundTrip.Run(ImportSetup.ImporterFor(gd), path, gd);
        if (!rt.Ok) throw new Exception("document round trip: " + rt.Error);
        GameObject root = asm.Build(rt.Input);
        var ctx = asm.Context;
        var b = new Built { Root = root, Ctx = ctx, Asm = asm, M = ctx.Motion, A = CharaAnimation.Of(root), Look = CharaLook.Of(root), Expr = CharaExpression.Of(root) };
        b.M.AutoStep = false;
        b.BodyRoot = CharaAssembler.Find(ctx.Anim.transform, "cf_J_Root");
        b.HeadRoot = ctx.HeadBone ? ctx.HeadBone.transform : null;
        // physics off: Studio disables every DynamicBone* (bones back to the start pose); the bridge then copies the
        // bust / hip reference transforms into the Ver02 chains every frame (kinematic pose)
        var dyn = b.M.Get<DynamicBoneStage>(); if (dyn != null) dyn.Enabled = false;
        b.Bust = b.M.Get<BustStage>(); if (b.Bust != null) b.Bust.Enabled = false;
        return b;
    }

    static void StepOnce(Built b)
    {
        b.M.Step(Dt);
        if (b.Bust != null) foreach (var c in b.Bust.All) c.InitTransforms();
        if (b.Ctx.Bones) b.Ctx.Bones.Apply();
    }

    // steps until every compared transform is still for 3 steps in a row (at least min, at most max steps)
    static int Settle(Built b, List<Transform> ts, int min, int max)
    {
        float[] last = null; int still = 0;
        for (int f = 1; f <= max; f++)
        {
            StepOnce(b);
            var sig = new float[ts.Count * 10];
            for (int i = 0; i < ts.Count; i++)
            {
                var t = ts[i]; if (!t) continue;
                var p = t.localPosition; var q = t.localRotation; var s = t.localScale;
                int k = i * 10;
                sig[k] = p.x; sig[k + 1] = p.y; sig[k + 2] = p.z; sig[k + 3] = q.x; sig[k + 4] = q.y; sig[k + 5] = q.z; sig[k + 6] = q.w; sig[k + 7] = s.x; sig[k + 8] = s.y; sig[k + 9] = s.z;
            }
            bool same = last != null && sig.SequenceEqual(last);
            still = same ? still + 1 : 0;
            last = sig;
            if (f >= min && still >= 3) return f;
        }
        return max;
    }

    // ================================================================ one case

    sealed class Rec { public string Name, Path; public Vector3 P, S; public Quaternion Q; public Transform T; }

    static void RunCase(ContentCatalog gd, string path, string dir, string cardName, string sex, string cs, Dictionary<string, object> data, bool render, string source = "document")
    {
        var sw = D(data, "switches");
        bool plugins = sw != null && B(sw, "plugins");
        rep.AppendLine($"-- {cs}{Tag(source)}: plugins {(plugins ? "on" : "off")}, physics off, look {(sw != null && B(sw, "look") ? "on" : "off")}");
        var b = Build(gd, path, plugins, source);
        try
        {
            // the HS2 dump and the matching HS2Next transforms
            var recs = new List<Rec>();
            var dump = D(data, "dump");
            foreach (var o in L(dump, "bones"))
            {
                var e = (Dictionary<string, object>)o;
                var r = new Rec { Name = S(e, "name"), Path = S(e, "path"), P = V3(e["lp"]), Q = Q4(e["lq"]), S = V3(e["ls"]) };
                r.T = Resolve(b, r.Path, r.Name);
                recs.Add(r);
            }
            var matched = recs.Where(r => r.T).ToList();

            // reproduce the case
            int frames = 0;
            var anim = b.A;
            if (anim == null) throw new Exception("no CharaAnimation on the character");
            anim.Speed = 0f;
            var row = D(data, "row");
            int g = row != null ? I(row, "group") : 0, c = row != null ? I(row, "category") : 0, n = row != null ? I(row, "no") : 0;
            float t = row != null ? (float)N(row, "normalizedTime") : 0f;
            var animeRow = CharaAnimation.Resolve(db, ContentIds.Vanilla, g, c, n);
            if (animeRow == null) throw new Exception($"anime row {g}/{c}/{n} not found");
            if (!anim.Load(animeRow, t)) throw new Exception($"anime {g}/{c}/{n}: load failed: {string.Join(" / ", anim.Log.Skip(Math.Max(0, anim.Log.Count - 2)))}");
            var look = b.Look;
            var expr = b.Expr;
            string eyeNote = InjectEyeStates(b, D(D(data, "face"), "eyeLook"));
            if (eyeNote.Length > 0) rep.AppendLine("   " + eyeNote);
            if (expr != null) expr.Blink = false;
            string lookNote = "";
            Transform camObj = null;
            if (cs == "look")
            {
                var lk = D(data, "look");
                var cam = D(lk, "camera");
                camObj = new GameObject("compare_look_camera").transform;
                camObj.position = V3(cam["position"]);
                camObj.rotation = Q4(cam["rotation"]);
                LookTargets.DefaultCamera = camObj;
                look.SetNeckPattern(1);
                look.SetEyePattern(4);
                look.SetNeckTarget(null);
                var local = V3(D(lk, "targetSetReply")["local"]);
                look.Targets.LookAtTargetLocalPosition = local;
                lookNote = $"camera at {camObj.position:F2}, look target local {local:F2} under cf_J_Head";
            }
            else
            {
                // Studio: eyes 0 / neck 4, then the look controllers are switched off, so the head shows the animation
                // pose and the eyes keep the forward pose; HS2Next: neck pattern 3 (animation) and eye pattern 0
                if (look != null) { look.SetNeckPattern(3); look.SetEyePattern(0); }
            }
            if (cs == "face" && expr != null)
            {
                var x = D(data, "expression");
                expr.SetEyebrowUiIndex(I(x, "eyebrowUi"));
                expr.SetEyesUiIndex(I(x, "eyesUi"));
                expr.SetStudioMouth(I(x, "mouth"));
                expr.SetMouthOpen((float)N(x, "mouthOpen"));
                expr.Blink = B(x, "blink");
                expr.Tears = (float)N(x, "tears");
            }
            frames = cs == "look" ? Settle(b, matched.Select(r => r.T).ToList(), 600, 1200)
                   : cs == "face" ? Settle(b, matched.Select(r => r.T).ToList(), 120, 600)
                   : Settle(b, matched.Select(r => r.T).ToList(), 20, 600);
            rep.AppendLine($"   card built; anime {g}/{c}/{n} t {t} (reads {anim.NormalizedTime:F4}), {frames} steps of 1/60 s{(lookNote.Length > 0 ? "; " + lookNote : "")}");
            var st = D(data, "state");
            if (st != null && D(st, "anime") is Dictionary<string, object> sa && sa.ContainsKey("normalizedTime"))
                rep.AppendLine($"   HS2: anime '{S(sa, "name")}' normalized time {N(sa, "normalizedTime"):F4}, root {Fmt(V3(st["worldPos"]))} rot {Fmt(V3(st["rot"]))}");

            boneMetrics[$"{cardName}|{cs}|{source}"] = CompareBones(cardName, cs + Tag(source), sex, recs, cs);
            if (data.ContainsKey("face")) CompareFace(b, cardName, cs + Tag(source), D(data, "face"));
            if (cs == "look") CompareLook(b, cardName + Tag(source), D(data, "face"));
            if (render && data.ContainsKey("visual")) Guard($"{cardName} {cs} render", () => Render(b, dir, cardName, cs, D(data, "visual")));
            if (camObj) Object.DestroyImmediate(camObj.gameObject);
            LookTargets.DefaultCamera = null;
        }
        finally { Object.DestroyImmediate(b.Root); }
    }

    // The game's EyeLookCalc type states as recorded (EyeLookAdjuster rewrites them in the reference install) copied into
    // the HS2Next eye stage; returns a note with the fields that differed from the bundle data ("" when equal or absent).
    static string InjectEyeStates(Built b, Dictionary<string, object> eyeLook)
    {
        var rec = L(D(eyeLook, "eyeLookScript"), "eyeTypeStates");
        var eyes = b.M.Get<EyeLookStage>();
        if (rec.Count == 0 || eyes?.States == null) return "";
        var diffs = new List<string>();
        for (int i = 0; i < rec.Count && i < eyes.States.Length; i++)
        {
            var r = (Dictionary<string, object>)rec[i]; var st = eyes.States[i];
            void Set(string key, ref float field)
            {
                float v = (float)N(r, key);
                if (v != field) diffs.Add($"{i}.{key} {field:G4}->{v:G4}");
                field = v;
            }
            Set("thresholdAngleDifference", ref st.Threshold); Set("bendingMultiplier", ref st.BendingMultiplier);
            Set("maxAngleDifference", ref st.MaxAngleDifference); Set("upBendingAngle", ref st.Up); Set("downBendingAngle", ref st.Down);
            Set("minBendingAngle", ref st.Min); Set("maxBendingAngle", ref st.Max); Set("leapSpeed", ref st.LeapSpeed);
            Set("forntTagDis", ref st.ForntTagDis); Set("nearDis", ref st.NearDis); Set("hAngleLimit", ref st.HAngleLimit); Set("vAngleLimit", ref st.VAngleLimit);
        }
        return diffs.Count == 0 ? "" : $"eye look: game EyeLookCalc states differ from the bundle data (EyeLookAdjuster), copied into HS2Next ({diffs.Count} fields): {string.Join(", ", diffs)}";
    }

    static Transform Resolve(Built b, string path, string name)
    {
        Transform root; string rel;
        if (path == "p_cf_head_bone" || path.StartsWith("p_cf_head_bone/")) { root = b.HeadRoot; rel = path.Length > 14 ? path.Substring(15) : ""; }
        else { root = b.BodyRoot; rel = path; }
        if (!root) return null;
        if (rel.Length == 0) return root.name == name ? root : null;
        var t = root.Find(rel);
        return t && t.name == name ? t : null;
    }

    // ================================================================ bones

    // skeleton = bones of the body / head rigs; parts = transforms inside hair, clothes and accessory objects
    // (any path segment ca_slot*, ct_*, or an accessory move node N_move*)
    static bool Skeleton(Rec r)
    {
        foreach (var seg in r.Path.Split('/'))
            if (seg.StartsWith("ca_slot") || seg.StartsWith("ct_") || seg.StartsWith("N_move")) return false;
        string n = r.Name;
        return n.StartsWith("cf_") || n.StartsWith("cm_") || n.StartsWith("N_") || n.StartsWith("k_f_") || n.StartsWith("p_cf_");
    }

    // Transforms whose recorded game value comes from something HS2Next does not implement (or from the recording
    // itself). They are reported with their values but left out of the PASS / FAIL decision. cases: null = every case.
    // sex: null = both.
    static readonly (string name, string[] cases, string sex, string reason)[] KnownDeviations =
    {
        ("cf_J_sk_04_01", null, null, "game 0, HS2Next keeps the p_cf_anim prefab value (-12 deg): Studio's FK initialisation sets every FK bone of a new character to identity, which stays where nothing else writes (verified in 2.3: the rest_identity case with FkOptions.ResetToIdentity matches the game within 0.1 deg); HS2Next's default keeps the rest pose"),
        ("cf_J_sk_04_02", null, null, "as cf_J_sk_04_01 (prefab -14 deg)"),
        ("cf_J_Kosi02_s", null, "male", "male only: the game holds a constant -0.28 deg about x (also over every Phase 1.7 shape sample); prefab 0, no clip binds it, the shape map has no rotation row for it; source not identified"),
        ("cm_J_dan109_00", new[] { "rest_plugins" }, null, "most likely UncensorSelector 'Default Female Penis = Random' swaps in a penis rig (34 extra transforms such as cm_J_dan102_00..cm_J_dan119_00 and cm_J_vdan*, and a shorter cm_J_dan109_00) while the part stays hidden on a female; HS2Next replaces no body part (the official build)"),
    };

    static string Known(string name, string cs, string sex)
    {
        foreach (var k in KnownDeviations)
            if (k.name == name && (k.cases == null || k.cases.Contains(cs)) && (k.sex == null || k.sex == sex)) return k.reason;
        return null;
    }

    /// <summary>Compares and reports; returns the metrics (max / mean per group, float bits) for the -source both check.</summary>
    static string CompareBones(string cardName, string label, string sex, List<Rec> recs, string cs)
    {
        float tolP = 1e-3f, tolA = cs.StartsWith("anime") ? 0.25f : cs == "look" ? 1f : 0.1f, tolS = 1e-3f;
        var matched = recs.Where(r => r.T).ToList();
        var missing = recs.Where(r => !r.T).ToList();
        var all = matched.Select(r => (r, dp: MaxAbs(r.T.localPosition - r.P), da: Angle(r.T.localRotation, r.Q), ds: MaxAbs(r.T.localScale - r.S))).ToList();
        var known = all.Where(x => Skeleton(x.r) && Known(x.r.Name, cs, sex) != null).ToList();
        var sig = new StringBuilder();
        var rows = all.Except(known).ToList();
        if (known.Count > 0)
        {
            rep.AppendLine($"   known deviations (not counted): {known.Count}");
            foreach (var x in known)
                rep.AppendLine($"     {x.r.Name,-30} pos {x.dp:G3}  angle {x.da:G3}  scale {x.ds:G3}   HS2 le {Fmt(x.r.Q.eulerAngles)} | HS2Next le {Fmt(x.r.T.localRotation.eulerAngles)}: {Known(x.r.Name, cs, sex)}");
        }
        foreach (var grp in new[] { "skeleton", "parts" })
        {
            var g = rows.Where(x => Skeleton(x.r) == (grp == "skeleton")).ToList();
            if (g.Count == 0) { rep.AppendLine($"   bones {grp}: none"); continue; }
            float mp = g.Max(x => x.dp), ma = g.Max(x => x.da), ms = g.Max(x => x.ds);
            double ep = g.Average(x => x.dp), ea = g.Average(x => x.da), es = g.Average(x => x.ds);
            sig.Append(grp).Append(' ').Append(g.Count).Append(' ').Append(string.Join(",", new[] { mp, ma, ms }.Select(v => BitConverter.ToInt32(BitConverter.GetBytes(v), 0).ToString(Inv))))
               .Append(' ').Append(string.Join(",", new[] { ep, ea, es }.Select(v => v.ToString("R", Inv)))).Append("; ");
            int over = g.Count(x => x.dp >= tolP || x.da >= tolA || x.ds >= tolS);
            string line = $"{g.Count} transforms; position max {mp:G3} mean {ep:G3}; angle max {ma:G3} deg mean {ea:G3}; scale max {ms:G3} mean {es:G3}; over threshold {over}";
            if (grp == "skeleton")
                Check($"{cardName} {label} bones (skeleton; thresholds pos {tolP}, angle {tolA} deg, scale {tolS})", over == 0,
                      line + (known.Count > 0 ? $"; known deviations not counted {known.Count} ({string.Join(", ", known.Select(x => $"{x.r.Name} {Math.Max(x.da, x.dp):G3}"))})" : ""));
            else rep.AppendLine($"   bones {grp} (hair / clothes / accessory transforms, info): {line}");
            var worst = g.OrderByDescending(x => Math.Max(x.dp / tolP, Math.Max(x.da / tolA, x.ds / tolS))).Take(15).ToList();
            rep.AppendLine($"   worst {grp}:");
            foreach (var x in worst)
                rep.AppendLine($"     {x.r.Name,-30} pos {x.dp:G3}  angle {x.da:G3}  scale {x.ds:G3}   HS2 lp {Fmt(x.r.P)} le {Fmt(x.r.Q.eulerAngles)} | HS2Next lp {Fmt(x.r.T.localPosition)} le {Fmt(x.r.T.localRotation.eulerAngles)}");
        }
        rep.AppendLine($"   HS2 transforms not found on the HS2Next character: {missing.Count}" +
                       (missing.Count > 0 ? " (" + string.Join(", ", missing.Take(15).Select(m => m.Name)) + (missing.Count > 15 ? ", ..." : "") + ")" : ""));
        sig.Append("missing ").Append(missing.Count);
        return sig.ToString();
    }

    // Quaternion.Angle reports 0 below about 0.1 deg; this keeps small angles.
    static float Angle(Quaternion a, Quaternion b)
    {
        var d = Quaternion.Inverse(a) * b;
        double v = Math.Sqrt((double)d.x * d.x + (double)d.y * d.y + (double)d.z * d.z);
        return (float)(2 * Math.Atan2(v, Math.Abs((double)d.w)) * 180 / Math.PI);
    }

    static float MaxAbs(Vector3 d) => Mathf.Max(Mathf.Abs(d.x), Mathf.Max(Mathf.Abs(d.y), Mathf.Abs(d.z)));

    // ================================================================ pose cases (Phase 2.3)

    sealed class PoseMetrics
    {
        public string Case, Source;
        public int FkCount, OtherCount;
        public float FkMax, HipsPos, OtherPos, OtherAngle, OtherScale;
        public double FkMean, OtherMeanAngle;
        public string Signature => string.Join("|", FkCount, OtherCount, Bits(FkMax), Bits(HipsPos), Bits(OtherPos), Bits(OtherAngle), Bits(OtherScale), FkMean.ToString("R", Inv), OtherMeanAngle.ToString("R", Inv));
        static string Bits(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0).ToString(Inv);
    }

    static readonly Dictionary<string, PoseMetrics> poseMetrics = new Dictionary<string, PoseMetrics>();

    static void RunPoseCases(ContentCatalog gd, string dir, string card, string sex, string source)
    {
        var files = Directory.GetFiles(dir, "pose_*.json").OrderBy(f => int.TryParse(Path.GetFileNameWithoutExtension(f).Substring(5), out var n) ? n : 999).ToList();
        if (files.Count == 0) { rep.AppendLine("-- pose cases: no data (Tools/hs2_compare.py --cases pose)"); return; }
        string path = Path.Combine(CardResolveTest.GameRoot, "UserData", "chara", sex == "male" ? "male" : "female", card.Replace('/', Path.DirectorySeparatorChar));
        string cardName = Path.GetFileName(dir);
        foreach (var f in files)
        {
            var data = (Dictionary<string, object>)Json.Parse(File.ReadAllText(f));
            string cs = S(data, "case");
            if (onlyCases != null && !onlyCases.Contains(cs)) continue;
            string invalid = S(data, "invalid");
            if (invalid.Length > 0) { rep.AppendLine($"-- {cs}: INVALID in the recording ({invalid}); reported, not counted"); summary.Add($"INFO  {cardName} {cs}: invalid recording ({invalid})"); continue; }
            foreach (var src in source == "both" ? new[] { "card", "document" } : new[] { source })
                Guard($"{cardName} {cs} ({src})", () => RunPoseCase(gd, path, cardName, sex, cs, data, src));
            if (source == "both" && poseMetrics.TryGetValue(cs + "|card", out var mc) && poseMetrics.TryGetValue(cs + "|document", out var md))
                Check($"{cardName} {cs}: metrics equal for card and document builds", mc.Signature == md.Signature, mc.Signature == md.Signature ? "" : $"card {mc.Signature} / document {md.Signature}");
        }
    }

    static void RunPoseCase(ContentCatalog gd, string cardPath, string cardName, string sex, string cs, Dictionary<string, object> data, string source)
    {
        var pose = D(data, "pose");
        string rel = S(pose, "path");
        string posePath = Path.Combine(CardResolveTest.GameRoot, "UserData", "studio", "pose", rel.Replace('/', Path.DirectorySeparatorChar));
        rep.AppendLine($"-- {cs} ({source}): {rel}; HS2 controllers off: {L(data, "controllersDisabled").Count}, ABMX removed {L(data, "abmxRemoved").Count}, {I(data, "framesAfterLoad")} frames after the load");
        if (!File.Exists(posePath)) { Fail($"{cardName} {cs} ({source}) pose file", rel); return; }
        var services = new RuntimeServices { Catalog = gd, Db = db, Hs2Root = CardResolveTest.GameRoot, Settings = new SceneRuntimeSettings { ManualStepping = true } };
        ConfigureExtOptions(services, data);
        var registration = ImportSetup.Register(services);
        var oldFk = CharaPoseDrivers.Fk;
        bool oldHips = FkOptions.HipsOffset;
        SceneRuntime rt = null;
        try
        {
            if (!(CharaPoseDrivers.Fk is FkDriver)) CharaPoseDrivers.Fk = FkDriver.Instance;
            FkOptions.HipsOffset = true;
            var s = SceneRuntimeTest.NewSession(services);
            rt = new SceneRuntime(s, services, null, false);
            var add = rt.Ops.AddCharaFromCard(cardPath);
            if (add == null || !add.Ok) { Fail($"{cardName} {cs} ({source}) add", add?.ToString()); return; }
            string id = add.NewId;
            rt.ApplyNow();
            var r = rt.FindChara(id);
            if (r == null || r.State != HS2Next.Scenes.Runtime.RealizationState.Built) { Fail($"{cardName} {cs} ({source}) build", r?.ToString()); return; }
            rep.AppendLine($"   built by {r.Builder?.GetType().Name} ({(r.Card != null ? "card" : "documents")})");
            // the recording's switches: T-pose 0/0/0 at speed 0, eye pattern 0, blink off, physics off
            s.SetAnimation(id, new HS2Next.Save.AnimeRef(ContentIds.Vanilla, 0, 0, 0));
            s.Set(id, "chara.anime.speed", 0f);
            s.Set(id, "chara.look.eyes", 0);
            s.Set(id, "chara.face.blink", false);
            rt.ApplyNow();
            var dyn = r.Motion.Get<DynamicBoneStage>(); if (dyn != null) dyn.Enabled = false;
            var bust = r.Motion.Get<BustStage>(); if (bust != null) bust.Enabled = false;
            var report = HS2Next.Scenes.Runtime.PoseImport.ApplyFile(rt, id, posePath);
            if (!report.Applied) { Fail($"{cardName} {cs} ({source}) pose import", report.Error); return; }
            var built = new Built { Root = r.Root, Ctx = r.Context, M = r.Motion, A = r.Animation, Look = r.Look, Expr = r.Expression, Bust = bust };
            built.BodyRoot = CharaAssembler.Find(r.Context.Anim.transform, "cf_J_Root");
            built.HeadRoot = r.Context.HeadBone ? r.Context.HeadBone.transform : null;
            var recs = new List<Rec>();
            foreach (var o in L(D(data, "dump"), "bones"))
            {
                var e = (Dictionary<string, object>)o;
                var x = new Rec { Name = S(e, "name"), Path = S(e, "path"), P = V3(e["lp"]), Q = Q4(e["lq"]), S = V3(e["ls"]) };
                x.T = Resolve(built, x.Path, x.Name);
                recs.Add(x);
            }
            int frames = SettleScene(rt, built, recs.Where(x => x.T).Select(x => x.T).ToList(), 20, 600);
            rep.AppendLine($"   import: {report}; {frames} steps of 1/60 s; applied bones {report.AppliedBones.Count}; findings {string.Join(" | ", report.Findings.Select(x => x.Code + (x.Count > 1 ? " x" + x.Count : "")))}");
            var fkc = FkDriver.Of(r);
            var st = (HS2Next.Save.CharaNodeState)s.Model.Find(id).State;

            // FK bones of the applied groups (+ partners, nodes present), the hips position, the other skeleton transforms
            var fkNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var k in report.AppliedBones)
            {
                var b = fkc?.Table.FindAny(k);
                if (b == null || !b.Bone || !b.Used) continue;
                fkNames.Add(b.Bone.name);
                if (b.Partner) fkNames.Add(b.Partner.name);
            }
            if (fkc != null) foreach (var b in fkc.Table.Additional) if (fkc.Stage.IsApplied(b.Group) && b.HasValue) fkNames.Add(b.Bone.name);
            var m = new PoseMetrics { Case = cs, Source = source };
            var fkRows = recs.Where(x => x.T && fkNames.Contains(x.Name)).Select(x => (x, da: Angle(x.T.localRotation, x.Q))).ToList();
            m.FkCount = fkRows.Count;
            m.FkMax = fkRows.Count > 0 ? fkRows.Max(x => x.da) : 0f;
            m.FkMean = fkRows.Count > 0 ? fkRows.Average(x => x.da) : 0;
            Check($"{cardName} {cs} ({source}) FK bones of the applied groups: rotation ≤ 0.1°", fkRows.Count > 0 && m.FkMax <= 0.1f,
                  $"{fkRows.Count} bones, max {m.FkMax:G3}° mean {m.FkMean:G3}°" + (fkRows.Count > 0 ? "; worst " + string.Join(", ", fkRows.OrderByDescending(x => x.da).Take(5).Select(x => $"{x.x.Name} {x.da:G3}")) : ""));
            var hips = recs.FirstOrDefault(x => x.Name == "cf_J_Hips" && x.T);
            m.HipsPos = hips != null ? MaxAbs(hips.T.localPosition - hips.P) : float.NaN;
            Check($"{cardName} {cs} ({source}) cf_J_Hips local position ≤ 1e-3 (hips offset {st.Fk.HipsOffset:G4})", hips != null && m.HipsPos <= 1e-3f,
                  hips == null ? "not found" : $"HS2 {Fmt(hips.P)} HS2Next {Fmt(hips.T.localPosition)} diff {m.HipsPos:G3}");
            var others = recs.Where(x => x.T && Skeleton(x) && !fkNames.Contains(x.Name)).ToList();
            var known = others.Where(x => Known(x.Name, "pose", sex) != null).ToList();
            // HS2's FK gizmo writes a bone once when a pose sets its value (GuideObject.CalcRotation on the ChangeAmount
            // callback), whatever its group; a bone no other writer touches keeps it (breast Nip bones with the Breast group
            // off, skirt bones the outfit does not weight). HS2Next writes only applied groups and used skirt bones:
            // such a bone is a known deviation when the game holds exactly the file's value.
            var oneShot = new List<Rec>();
            if (fkc != null)
                foreach (var x in others.Except(known))
                {
                    var b = fkc.Table.FindAny(x.Name);
                    if (b == null || !b.HasValue || (fkc.Stage.IsApplied(b.Group) && b.Used)) continue;
                    if (Angle(x.Q, Quaternion.Euler(b.Value)) <= 0.1f && Angle(x.T.localRotation, x.Q) > 0.1f) oneShot.Add(x);
                }
            if (oneShot.Count > 0)
                rep.AppendLine($"   known deviation (HS2's FK gizmo writes a value once when the pose sets it, also outside the applied groups / on unweighted skirt bones; the game holds the file's value): {string.Join(", ", oneShot.Select(x => $"{x.Name} {Angle(x.T.localRotation, x.Q):G3}°"))}");
            known.AddRange(oneShot);
            var rows = others.Except(known).Select(x => (x, dp: x.Name == "cf_J_Hips" ? 0f : MaxAbs(x.T.localPosition - x.P), da: Angle(x.T.localRotation, x.Q), ds: MaxAbs(x.T.localScale - x.S))).ToList();
            m.OtherCount = rows.Count;
            m.OtherPos = rows.Count > 0 ? rows.Max(x => x.dp) : 0; m.OtherAngle = rows.Count > 0 ? rows.Max(x => x.da) : 0; m.OtherScale = rows.Count > 0 ? rows.Max(x => x.ds) : 0;
            m.OtherMeanAngle = rows.Count > 0 ? rows.Average(x => x.da) : 0;
            int over = rows.Count(x => x.dp >= 1e-3f || x.da >= 0.25f || x.ds >= 1e-3f);
            Check($"{cardName} {cs} ({source}) other skeleton transforms (pos 1e-3, angle 0.25°, scale 1e-3)", over == 0,
                  $"{rows.Count} transforms; position max {m.OtherPos:G3}; angle max {m.OtherAngle:G3}° mean {m.OtherMeanAngle:G3}°; scale max {m.OtherScale:G3}; over {over}" +
                  (known.Count > 0 ? $"; known deviations not counted {known.Count} ({string.Join(", ", known.Select(x => x.Name))})" : ""));
            foreach (var x in rows.OrderByDescending(x => Math.Max(x.dp / 1e-3f, Math.Max(x.da / 0.25f, x.ds / 1e-3f))).Take(over > 0 ? 15 : 5))
                rep.AppendLine($"     {x.x.Name,-30} pos {x.dp:G3}  angle {x.da:G3}  scale {x.ds:G3}   HS2 lp {Fmt(x.x.P)} le {Fmt(x.x.Q.eulerAngles)} | HS2Next lp {Fmt(x.x.T.localPosition)} le {Fmt(x.x.T.localRotation.eulerAngles)}");

            // face ids and opens, correction, flags
            var hx = D(D(data, "face"), "expression");
            var ex = r.Expression;
            if (hx != null && ex != null)
            {
                bool ok = I(hx, "eyebrowPtn") == ex.Status.EyebrowPtn && I(hx, "eyesPtn") == ex.Status.EyesPtn && I(hx, "mouthPtn") == ex.Status.MouthPtn &&
                          (float)N(hx, "eyesOpenMax") == ex.Status.EyesOpenMax && (float)N(hx, "studioMouthOpen") == ex.MouthOpen;
                Check($"{cardName} {cs} ({source}) face: eyebrow, eyes, mouth ids, eyes open, mouth open", ok,
                      $"HS2 {I(hx, "eyebrowPtn")}/{I(hx, "eyesPtn")}/{I(hx, "mouthPtn")} open {N(hx, "eyesOpenMax"):R}/{N(hx, "studioMouthOpen"):R}; HS2Next {ex.Status.EyebrowPtn}/{ex.Status.EyesPtn}/{ex.Status.MouthPtn} open {ex.Status.EyesOpenMax:R}/{ex.MouthOpen:R}");
            }
            var fk = D(data, "fk");
            var hsFk = L(fk, "activeFK").Select(x => (bool)x).ToArray();
            var hsCorr = L(fk, "expression").Select(x => (bool)x).ToArray();
            var g = st.Fk.Groups;
            var nxFk = new[] { g.Hair, g.Neck, g.Breast, g.Body, g.RightHand, g.LeftHand, g.Skirt };
            Check($"{cardName} {cs} ({source}) correction[0..7], fk.enabled, ik.enabled, the seven group flags", hsCorr.SequenceEqual(st.Correction) && hsFk.SequenceEqual(nxFk) &&
                  B(fk, "enableFK") == st.Fk.Enabled && B(fk, "enableIK") == st.Ik.Enabled,
                  $"HS2 corr {Flags(hsCorr)} fk {B(fk, "enableFK")} ik {B(fk, "enableIK")} groups {Flags(hsFk)} | HS2Next corr {Flags(st.Correction)} fk {st.Fk.Enabled} ik {st.Ik.Enabled} groups {Flags(nxFk)}");

            // fk.list rot / pos vs the file vs the model (float ==)
            var file = HS2Next.Import.Pose.PoseFile.ReadFile(posePath);
            var map = HS2Next.Scenes.Runtime.PoseImport.MapFor(FkRows.From(db), HS2Next.Chara.Ext.CharaBuildHooks.FkNodes(gd)?.Rows, true).MapFk(file, sex == "male" ? 0 : 1);
            var hsByKey = new Dictionary<int, Dictionary<string, object>>();
            foreach (var o in L(fk, "bones")) { var e = (Dictionary<string, object>)o; hsByKey[I(e, "key")] = e; }
            int compared = 0; var bad = new List<string>();
            foreach (var e in map.Entries)
            {
                if (!e.Applied) continue;
                compared++;
                var rot = e.Entry.Rot;
                if (!hsByKey.TryGetValue(e.TargetKey, out var h)) { bad.Add($"{e.Key}: not in HS2's fk.list"); continue; }
                var hr = V3(h["rot"]);
                bool fileEq = hr.x == rot.X && hr.y == rot.Y && hr.z == rot.Z;
                bool has = st.Fk.Bones.TryGetValue(e.Bone.TransformName, out var mv);
                bool modelEq = has && mv.X == rot.X && mv.Y == rot.Y && mv.Z == rot.Z;
                if (!fileEq || !modelEq) bad.Add($"{e.Key}→{e.TargetKey} {e.Bone.TransformName}: file {rot} HS2 {Fmt(hr)} model {(has ? mv.ToString() : "-")}");
            }
            var tools = file.PoseTools;
            if (tools?.SkirtFK != null && (file.GameName ?? 0) >= 3 && (file.GameName ?? 0) <= 4)
                foreach (var sk in tools.SkirtFK)
                {
                    compared++;
                    var rot = sk.Rot;
                    var name = db.FindBone(sk.Key)?.TransformName;
                    bool hsEq = hsByKey.TryGetValue(sk.Key, out var h) && V3(h["rot"]) is Vector3 hr && hr.x == rot.X && hr.y == rot.Y && hr.z == rot.Z;
                    bool modelEq = name != null && st.Fk.Bones.TryGetValue(name, out var mv) && mv.X == rot.X && mv.Y == rot.Y && mv.Z == rot.Z;
                    if (!hsEq || !modelEq) bad.Add($"skirt {sk.Key} {name}: file {rot}");
                }
            var k0 = file.FindFk(0);
            bool posEq = k0 == null || (hsByKey.TryGetValue(0, out var h0) && V3(h0["pos"]).y == k0.Pos.Y && st.Fk.HipsOffset == k0.Pos.Y);
            if (!posEq) bad.Add($"key 0 pos.y: file {k0?.Pos.Y:R} HS2 {(hsByKey.TryGetValue(0, out var hh) ? V3(hh["pos"]).y.ToString("R", Inv) : "-")} model {st.Fk.HipsOffset:R}");
            Check($"{cardName} {cs} ({source}) HS2 fk.list rot / pos = file = HS2Next model (float ==)", bad.Count == 0 && compared > 0, $"{compared} values; " + string.Join("; ", bad.Take(8)));

            // report
            var fam = report.UnresolvedFamilies.Keys.ToList();
            bool famOk = fam.All(x => x == nameof(HS2Next.Import.Pose.PoseKeyFamily.AiShoujo) || x == nameof(HS2Next.Import.Pose.PoseKeyFamily.AdditionalFkNodes));
            // the anime record: resolved exactly when HS2 played the file's record (0/2/11, hidden in the lists, plays in both)
            var hsAnime = D(D(data, "state"), "anime");
            var row = D(data, "row");
            bool hsPlayed = hsAnime != null && row != null && I(hsAnime, "group") == I(row, "group") && I(hsAnime, "category") == I(row, "category") && I(hsAnime, "no") == I(row, "no");
            bool animeOk = hsPlayed ? report.AnimeResolved != null : report.AnimeResolved == null;
            Check($"{cardName} {cs} ({source}) report: unresolved keys only of the expected families; anime {(hsPlayed ? "resolved (HS2 played the record)" : "kept (HS2 kept the motion)")}", famOk && animeOk,
                  $"unresolved {string.Join(", ", report.UnresolvedFamilies.Select(kv => kv.Key + " " + kv.Value))}; anime {report.AnimeResolved ?? "kept"}");
            poseMetrics[cs + "|" + source] = m;
        }
        finally
        {
            rt?.Dispose();
            registration?.Dispose();
            CharaPoseDrivers.Fk = oldFk;
            FkOptions.HipsOffset = oldHips;
        }
    }

    // The recording's controller switches → the extension modules: abmxClear → card
    // ABMX off; the AdditionalAccessoryControls controller (bone effects) → AAC off. Texture-only controllers switched off
    // after the load (KSOX, KCOX, MaterialEditor) do not undo what they applied in HS2, so those modules stay on.
    static void ConfigureExtOptions(RuntimeServices services, Dictionary<string, object> data)
    {
        var o = services.ExtOptions = new CharaExtOptions();
        var sw = D(data, "switches");
        if (sw == null || B(sw, "abmxClear")) o.Abmx = false;
        var off = new HashSet<string>(L(data, "controllersDisabled").Select(x => x?.ToString() ?? ""));
        if (off.Contains("AdditionalAccessoryControlsController")) o.AccessoryControls = false;
        rep.AppendLine($"   HS2Next modules: ABMX {(o.Abmx ? "on" : "off")}, AAC {(o.AccessoryControls ? "on" : "off")}");
    }

    static string Flags(bool[] a) => string.Concat(a.Select(x => x ? "1" : "0"));

    // Tick(1/60) until every compared transform is still for 3 steps (as Settle), with StepOnce's per-step copies
    static int SettleScene(SceneRuntime rt, Built b, List<Transform> ts, int min, int max)
    {
        float[] last = null; int still = 0;
        for (int f = 1; f <= max; f++)
        {
            rt.Tick(Dt);
            if (b.Bust != null) foreach (var c in b.Bust.All) c.InitTransforms();
            if (b.Ctx.Bones) b.Ctx.Bones.Apply();
            var sig = new float[ts.Count * 10];
            for (int i = 0; i < ts.Count; i++)
            {
                var t = ts[i]; if (!t) continue;
                var p = t.localPosition; var q = t.localRotation; var s = t.localScale;
                int k = i * 10;
                sig[k] = p.x; sig[k + 1] = p.y; sig[k + 2] = p.z; sig[k + 3] = q.x; sig[k + 4] = q.y; sig[k + 5] = q.z; sig[k + 6] = q.w; sig[k + 7] = s.x; sig[k + 8] = s.y; sig[k + 9] = s.z;
            }
            bool same = last != null && sig.SequenceEqual(last);
            still = same ? still + 1 : 0;
            last = sig;
            if (f >= min && still >= 3) return f;
        }
        return max;
    }

    // ================================================================ face

    static void CompareFace(Built b, string cardName, string cs, Dictionary<string, object> face)
    {
        var head = b.Ctx.Head ? b.Ctx.Head.transform : null;
        if (!head) { Fail($"{cardName} {cs} face", "no head object on the HS2Next character"); return; }
        var smrs = head.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        int shapes = 0, equal = 0; var bad = new List<(string, float, float)>(); var noRend = new List<string>(); var noShape = new List<string>();
        var mat = new List<string>();
        foreach (var o in L(face, "renderers"))
        {
            var e = (Dictionary<string, object>)o;
            string rn = S(e, "name");
            var smr = smrs.FirstOrDefault(s => s.name == rn);
            var names = L(e, "blendShapes"); var ws = L(e, "weights");
            if (!smr || !smr.sharedMesh) { if (names.Count > 0) noRend.Add(rn); continue; }
            for (int i = 0; i < names.Count; i++)
            {
                string sn = (string)names[i]; float w = (float)Convert.ToDouble(ws[i], Inv);
                int idx = smr.sharedMesh.GetBlendShapeIndex(sn);
                shapes++;
                if (idx < 0) { noShape.Add(rn + ":" + sn); continue; }
                float h = smr.GetBlendShapeWeight(idx);
                if (h == w) equal++; else bad.Add((rn + ":" + sn, w, h));
            }
            // face material values the expression drives
            var mats = L(e, "materials");
            var sm = smr.sharedMaterials;
            for (int i = 0; i < mats.Count && i < sm.Length; i++)
            {
                if (!(mats[i] is Dictionary<string, object> md) || !sm[i]) continue;
                foreach (var p in new[] { "_NamidaScale", "_Texture4Scale", "_Smoothness" })
                    if (md.ContainsKey(p)) mat.Add($"{rn}[{i}].{p} HS2 {N(md, p):G4} HS2Next {(sm[i].HasProperty(p) ? sm[i].GetFloat(p).ToString("G4", Inv) : "-")}");
            }
        }
        bool ok = bad.Count == 0 && noRend.Count == 0 && noShape.Count == 0;
        Check($"{cardName} {cs} blend shapes (exact)", ok, $"{shapes} shapes on {L(face, "renderers").Count} renderers, equal {equal}, different {bad.Count}, renderer missing {noRend.Count}, shape missing {noShape.Count}");
        foreach (var x in bad.OrderByDescending(x => Math.Abs(x.Item2 - x.Item3)).Take(15)) rep.AppendLine($"     {x.Item1,-40} HS2 {x.Item2:G4}  HS2Next {x.Item3:G4}");
        if (noRend.Count > 0) rep.AppendLine("     renderers missing: " + string.Join(", ", noRend));
        if (noShape.Count > 0) rep.AppendLine("     shapes missing: " + string.Join(", ", noShape.Take(10)));
        if (mat.Count > 0) rep.AppendLine("   materials: " + string.Join("; ", mat));
        var x2 = D(face, "expression");
        if (x2 != null && b.Expr != null)
            rep.AppendLine($"   expression HS2: eyebrow {I(x2, "eyebrowPtn")} eyes {I(x2, "eyesPtn")} mouth {I(x2, "mouthPtn")} mouthOpen {N(x2, "studioMouthOpen"):G3} blink {B(x2, "blinkFlag")} tears {N(x2, "tearsRate"):G3} look eyes {I(x2, "lookEyesPtn")} neck {I(x2, "lookNeckPtn")}" +
                           $" | HS2Next: eyebrow {b.Expr.Status.EyebrowPtn} eyes {b.Expr.Status.EyesPtn} mouth {b.Expr.Status.MouthPtn} mouthOpen {b.Expr.MouthOpen:G3} blink {b.Expr.Blink} tears {b.Expr.Tears:G3} look eyes {b.Look?.EyePattern} neck {b.Look?.NeckPattern}");
    }

    // ================================================================ look

    static void CompareLook(Built b, string cardName, Dictionary<string, object> face)
    {
        var look = b.Look;
        var eye = D(face, "eyeLook"); var neck = D(face, "neckLook");
        if (look == null || eye == null || neck == null) { Fail($"{cardName} look angles", "no look data"); return; }
        var lines = new List<string>(); float worst = 0;
        var eyeObjs = L(D(eye, "eyeLookScript"), "eyeObjs");
        for (int i = 0; i < 2 && i < eyeObjs.Count; i++)
        {
            var e = (Dictionary<string, object>)eyeObjs[i];
            float h = (float)N(e, "angleH"), v = (float)N(e, "angleV");
            var a = look.EyeAngle(i);
            worst = Mathf.Max(worst, Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(h, a.x)), Mathf.Abs(Mathf.DeltaAngle(v, a.y))));
            lines.Add($"eye {(i == 0 ? "L" : "R")} HS2 ({h:F3}, {v:F3}) HS2Next ({a.x:F3}, {a.y:F3})");
        }
        var bones = L(D(neck, "neckLookScript"), "aBones");
        for (int i = 0; i < bones.Count; i++)
        {
            var e = (Dictionary<string, object>)bones[i];
            float h = (float)N(e, "angleH"), v = (float)N(e, "angleV");
            var a = look.NeckBoneAngle(i);
            worst = Mathf.Max(worst, Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(h, a.x)), Mathf.Abs(Mathf.DeltaAngle(v, a.y))));
            lines.Add($"neck bone {i} HS2 ({h:F3}, {v:F3}) HS2Next ({a.x:F3}, {a.y:F3})");
        }
        Check($"{cardName} look angles (H, V; threshold 1 deg)", worst < 1f, $"worst difference {worst:F3} deg: " + string.Join("; ", lines));
        var lt = look.Targets?.LookAtTarget;
        if (lt) rep.AppendLine($"   look-at target world: HS2Next {Fmt(lt.position)}");
    }

    // ================================================================ render

    static void Render(Built b, string dir, string cardName, string cs, Dictionary<string, object> visual)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Fail("render", "needs a graphics device (no -nographics)"); return; }
        // the measurement asset only for these renders: the project's pipeline comes back in every case (an exception too)
        var prevDefault = GraphicsSettings.defaultRenderPipeline;
        var prevQuality = QualitySettings.renderPipeline;
        try { RenderViews(b, dir, cardName, cs, visual); }
        finally { GraphicsSettings.defaultRenderPipeline = prevDefault; QualitySettings.renderPipeline = prevQuality; }
    }

    static void RenderViews(Built b, string dir, string cardName, string cs, Dictionary<string, object> visual)
    {
        ShaderUtil.allowAsyncCompilation = false;
        var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HS2Next.App.AppPipeline.MeasureAssetPath);
        HS2Next.ShaderMap.ApplyUrp(b.Root);
        b.Expr?.RefreshMaterials();
        foreach (var s in b.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) s.forceMatrixRecalculationPerRender = true;
        GraphicsSettings.defaultRenderPipeline = asset; QualitySettings.renderPipeline = asset;
        foreach (var kv in visual)
        {
            string view = kv.Key;
            var v = (Dictionary<string, object>)kv.Value;
            var cam = D(v, "camera");
            var cap = D(v, "capture");
            int w = I(cap, "width"), h = I(cap, "height");
            var created = new List<GameObject>();
            string lightNote = SetupLights(D(v, "lights"), created);
            var camGo = new GameObject("compare_cam"); created.Add(camGo);
            var c = camGo.AddComponent<Camera>();
            c.transform.position = V3(cam["position"]); c.transform.rotation = Q4(cam["rotation"]);
            c.fieldOfView = (float)N(cam, "fov"); c.nearClipPlane = (float)N(cam, "near"); c.farClipPlane = (float)N(cam, "far");
            c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = C4(cam["background"]);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            c.targetTexture = rt; c.aspect = w / (float)h;
            StepOnce(b);   // the stages write once more after the material swap
            c.Render(); c.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = null; c.targetTexture = null;
            string prefix = Path.Combine(OutDir, $"{cardName}_{cs}_{view}");
            File.WriteAllBytes(prefix + "_hs2next.png", tex.EncodeToPNG());
            var next = tex.GetPixels32();
            string r1 = SideBySide(Path.Combine(dir, S(v, "pngNoFx")), next, w, h, prefix + ".png", prefix + "_diff.png");
            string r2 = SideBySide(Path.Combine(dir, S(v, "png")), next, w, h, prefix + "_fx.png", prefix + "_fx_diff.png");
            table.Add($"{cardName,-18} {cs,-12} {view,-5} {r1,-24} {r2,-24}");
            rep.AppendLine($"   render {view}: camera {Fmt(c.transform.position)} euler {Fmt(c.transform.eulerAngles)} fov {c.fieldOfView:G4} aspect {c.aspect:G4} (HS2 screen aspect {N(cam, "aspect"):G4}); lights: {lightNote}");
            rep.AppendLine($"   {Path.GetFileName(prefix)}.png: vs HS2 no effects {r1}; vs HS2 with effects {r2}");
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            foreach (var go in created) Object.DestroyImmediate(go);
        }
        GraphicsSettings.defaultRenderPipeline = null; QualitySettings.renderPipeline = null;
    }

    // directional / point / spot lights that were on in Studio, and the ambient (Unity RenderSettings as recorded)
    static string SetupLights(Dictionary<string, object> rec, List<GameObject> created)
    {
        var notes = new List<string>();
        foreach (var o in L(rec, "lights"))
        {
            var l = (Dictionary<string, object>)o;
            if (!B(l, "enabled") || !B(l, "active") || N(l, "intensity") <= 0) continue;
            var go = new GameObject("compare_" + S(l, "name")); created.Add(go);
            var li = go.AddComponent<Light>();
            li.type = (LightType)Enum.Parse(typeof(LightType), S(l, "type"));
            li.color = C4(l["color"]); li.intensity = (float)N(l, "intensity");
            li.range = (float)N(l, "range"); li.spotAngle = (float)N(l, "spotAngle");
            go.transform.position = V3(l["position"]); go.transform.rotation = Q4(l["rotation"]);
            li.shadows = (LightShadows)Enum.Parse(typeof(LightShadows), S(l, "shadows"));
            li.shadowStrength = (float)N(l, "shadowStrength");
            li.shadowBias = (float)N(l, "shadowBias"); li.shadowNormalBias = (float)N(l, "shadowNormalBias");
            notes.Add($"{S(l, "name")} {li.type} {li.intensity:G3} {Fmt(go.transform.forward)} shadows {li.shadows}");
        }
        var rs = D(rec, "renderSettings");
        string amb = "-";
        if (rs != null)
        {
            var mode = (AmbientMode)Enum.Parse(typeof(AmbientMode), S(rs, "ambientMode"));
            float k = (float)N(rs, "ambientIntensity");
            Color sky = C4(rs["ambientSkyColor"]), eq = C4(rs["ambientEquatorColor"]), gr = C4(rs["ambientGroundColor"]);
            RenderSettings.ambientMode = mode;
            RenderSettings.ambientSkyColor = sky; RenderSettings.ambientEquatorColor = eq; RenderSettings.ambientGroundColor = gr;
            RenderSettings.ambientIntensity = k;
            var probe = L(rs, "ambientProbe").Select(x => (float)Convert.ToDouble(x, Inv)).ToArray();
            var sh = new SphericalHarmonicsL2();
            bool recorded = probe.Length == 27 && probe.Any(x => x != 0);
            if (recorded) { for (int ch = 0; ch < 3; ch++) for (int i = 0; i < 9; i++) sh[ch, i] = probe[ch * 9 + i]; amb = "recorded SH probe"; }
            else
            {
                // HS2 reports an all-zero ambient probe for Studio's empty scene: let Unity derive the probe from the
                // trilight colours (DynamicGI.UpdateEnvironment); a flat average when that leaves it empty
                DynamicGI.UpdateEnvironment();
                sh = RenderSettings.ambientProbe;
                bool any = false;
                for (int ch = 0; ch < 3; ch++) for (int i = 0; i < 9; i++) any |= sh[ch, i] != 0;
                if (!any) { sh = new SphericalHarmonicsL2(); sh.AddAmbientLight(((sky + gr) * 0.25f + eq * 0.5f).linear * k); }
                amb = $"{mode} from the recorded colours ({(any ? "Unity probe" : "flat average")}; HS2 probe is all zero): sky {Fmt(sky)} equator {Fmt(eq)} ground {Fmt(gr)} x {k:G3}";
            }
            RenderSettings.ambientProbe = sh;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = ShaderProbe.BlackCube; RenderSettings.reflectionIntensity = 0;
            RenderSettings.fog = false;
        }
        return string.Join("; ", notes) + "; ambient " + amb;
    }

    // HS2 capture (left) and HS2Next (right) side by side, plus the per-pixel difference x4; returns "whole / foreground" MAD
    static string SideBySide(string hs2File, Color32[] next, int w, int h, string outFile, string diffFile)
    {
        if (!File.Exists(hs2File)) return "no HS2 capture";
        var a = new Texture2D(2, 2); a.LoadImage(File.ReadAllBytes(hs2File));
        if (a.width != w || a.height != h) { Object.DestroyImmediate(a); return $"size {a.width}x{a.height} != {w}x{h}"; }
        var pa = a.GetPixels32();
        var sbs = new Color32[w * 2 * h]; var pd = new Color32[w * h];
        var bgA = pa[0]; var bgB = next[0];
        bool Bg(Color32 c, Color32 bg) => Math.Abs(c.r - bg.r) <= 3 && Math.Abs(c.g - bg.g) <= 3 && Math.Abs(c.b - bg.b) <= 3;
        double sum = 0, fsum = 0; long fn = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                var p = pa[i]; var q = next[i];
                sbs[y * 2 * w + x] = p; sbs[y * 2 * w + w + x] = q;
                int dr = Math.Abs(p.r - q.r), dg = Math.Abs(p.g - q.g), db = Math.Abs(p.b - q.b);
                pd[i] = new Color32((byte)Math.Min(255, dr * 4), (byte)Math.Min(255, dg * 4), (byte)Math.Min(255, db * 4), 255);
                double m = (dr + dg + db) / 3.0;
                sum += m;
                if (!Bg(p, bgA) || !Bg(q, bgB)) { fsum += m; fn++; }
            }
        var o = new Texture2D(w * 2, h, TextureFormat.RGBA32, false); o.SetPixels32(sbs); o.Apply();
        File.WriteAllBytes(outFile, o.EncodeToPNG());
        var d = new Texture2D(w, h, TextureFormat.RGBA32, false); d.SetPixels32(pd); d.Apply();
        File.WriteAllBytes(diffFile, d.EncodeToPNG());
        Object.DestroyImmediate(a); Object.DestroyImmediate(o); Object.DestroyImmediate(d);
        return $"{sum / (w * h):F2} / {fsum / Math.Max(1, fn):F2}";
    }

    // ================================================================ JSON helpers

    static Dictionary<string, object> D(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) ? v as Dictionary<string, object> : null;
    static List<object> L(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) && v is List<object> l ? l : new List<object>();
    static string S(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) && v != null ? v.ToString() : "";
    static double N(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) && v != null ? Convert.ToDouble(v, Inv) : 0;
    static int I(Dictionary<string, object> d, string k) => (int)Math.Round(N(d, k));
    static bool B(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) && v is bool x && x;
    static Vector3 V3(object o) { var l = (List<object>)o; return new Vector3(F(l[0]), F(l[1]), F(l[2])); }
    static Quaternion Q4(object o) { var l = (List<object>)o; return new Quaternion(F(l[0]), F(l[1]), F(l[2]), F(l[3])); }
    static Color C4(object o) { var l = (List<object>)o; return new Color(F(l[0]), F(l[1]), F(l[2]), l.Count > 3 ? F(l[3]) : 1f); }
    static float F(object o) => (float)Convert.ToDouble(o, Inv);
    static string Fmt(Vector3 v) => $"({v.x:F4}, {v.y:F4}, {v.z:F4})";
    static string Fmt(Color c) => $"({c.r:F3}, {c.g:F3}, {c.b:F3})";

    /// <summary>Minimal JSON reader: objects -> Dictionary&lt;string, object&gt;, arrays -> List&lt;object&gt;, numbers -> double.</summary>
    static class Json
    {
        public static object Parse(string s) { int i = 0; var v = Value(s, ref i); return v; }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(); i++;
                Ws(s, ref i); if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i); string k = Str(s, ref i); Ws(s, ref i); i++; // ':'
                    d[k] = Value(s, ref i); Ws(s, ref i);
                    if (s[i++] == '}') return d;
                }
            }
            if (c == '[')
            {
                var l = new List<object>(); i++;
                Ws(s, ref i); if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i)); Ws(s, ref i);
                    if (s[i++] == ']') return l;
                }
            }
            if (c == '"') return Str(s, ref i);
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            foreach (var (tok, val) in new[] { ("NaN", double.NaN), ("Infinity", double.PositiveInfinity), ("-Infinity", double.NegativeInfinity) })
                if (string.CompareOrdinal(s, i, tok, 0, tok.Length) == 0) { i += tok.Length; return val; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            string num = s.Substring(st, i - st);
            if (num.Length == 0) throw new FormatException("bad JSON value at " + st);
            return double.Parse(num, NumberStyles.Float, Inv);
        }

        static string Str(string s, ref int i)
        {
            var sb = new StringBuilder(); i++;
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            i++;
            return sb.ToString();
        }
    }
}
