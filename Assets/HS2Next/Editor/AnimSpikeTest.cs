// Technical spike for Phase 1.6 (Studio character animation playback), written before the behavioural spec:
// can the HS2 (Unity 2018.4) Studio anime controllers be loaded through the catalog and played on the
// p_cf_anim Animator of an HS2Next character in batch edit mode by calling Animator.Update(dt)?
// Questions and the numbers that answer them go to Spike/out/motion/anim_spike.txt:
//   1. controller load (warnings, null), contents (clips, layers, parameters, state found) of 3 vanilla rows of
//      anime group 0 (non-H): a standing row, a walk / run row, a sitting row (picked by name; -rows g/c/n,... overrides);
//   2. BR-Chan headless: Animator switches needed (enabled, culling mode), bones moving at normalized times
//      0 / 0.25 / 0.5 / 0.75, root motion, shape bones (cf_J_*_s) untouched;
//   3. interplay with the motion stages (joint correction, neck look, dynamic bones) over 120 frames;
//   4. -render: full-body URP renders of BR-Chan, standing / walking at t = 0 and 0.5 (Spike/out/motion/anim_spike_<row>_<t>.png);
//   5. one mod controller from a zipmod bundle (load + play, no render);
//   6. timings (controller load, first Update, per-frame Update).
// Only Anime rows (IsH == false) are ever played; only BR-Chan is built.
// Run: -executeMethod AnimSpikeTest.Run [-render] [-card <png>] [-rows g/c/n,g/c/n,g/c/n]
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
using HS2Next.Motion.Corrective;
using HS2Next.Motion.DynamicBones;
using HS2Next.Motion.Look;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class AnimSpikeTest
{
    const float Dt = 1f / 60f;
    static StringBuilder rep;
    static int logWarn, logErr, legacyBundleWarnings;
    static readonly List<string> logSamples = new List<string>();

    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "motion");

    static string Arg(string name, string def)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    static void OnLog(string msg, string stack, LogType type)
    {
        if (type == LogType.Log) return;
        if (type == LogType.Warning) logWarn++; else logErr++;
        string first = msg.Split('\n')[0];
        if (first.StartsWith("Serialized files [version 17]")) { legacyBundleWarnings++; return; }
        if (logSamples.Count < 40) logSamples.Add($"[{type}] {first}");
    }

    static (int w, int e) LogMark() => (logWarn, logErr);
    static string LogSince((int w, int e) m) => $"warnings {logWarn - m.w}, errors {logErr - m.e}";

    static void Section(string s) { rep.AppendLine(); rep.AppendLine("== " + s); }
    static void Line(string s) => rep.AppendLine("   " + s);

    public static void Run()
    {
        rep = new StringBuilder(); logWarn = logErr = legacyBundleWarnings = 0; logSamples.Clear();
        Directory.CreateDirectory(OutDir);
        bool render = Environment.GetCommandLineArgs().Contains("-render");
        Application.logMessageReceived += OnLog;
        var head = new StringBuilder();
        ContentCatalog gd = null;
        try
        {
            gd = CardResolveTest.BuildDb(head);
            var sw = Stopwatch.StartNew();
            var db = gd.LoadStudioLists();
            rep.AppendLine($"anim spike (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}, batch {Application.isBatchMode}, render {(render ? "on" : "off")})");
            rep.Append(head);
            rep.AppendLine($"studio lists {sw.ElapsedMilliseconds} ms; {db.Summary().ElementAt(3)}");
            List<StudioAnime> rows = null;
            Guard("anime list", () => rows = PickRows(db));
            var ctrls = new Dictionary<StudioAnime, RuntimeAnimatorController>();
            if (rows != null) Guard("controllers", () => Controllers(gd, rows, ctrls));
            string card = Arg("-card", Path.Combine(CardResolveTest.GameRoot, @"UserData\chara\female\BR-Chan HS2 Rev2.png"));
            if (rows != null) Guard("card", () => CardSpike(gd, db, card, rows, ctrls, render));
        }
        finally
        {
            Application.logMessageReceived -= OnLog;
            LookTargets.DefaultCamera = null;
        }
        Section("log messages captured (warnings / errors, first 40)");
        Line($"total warnings {logWarn}, errors {logErr}; of the warnings {legacyBundleWarnings} are \"Serialized files [version 17] before 2019.1 are deprecated\" (one per opened 2018.4 bundle)");
        foreach (var l in logSamples) Line(l);
        File.WriteAllText(Path.Combine(OutDir, "anim_spike.txt"), rep.ToString());
        gd?.UnloadAll();
        Debug.Log("ANIM SPIKE DONE");
    }

    static void Guard(string what, Action a)
    {
        try { a(); }
        catch (Exception e) { rep.AppendLine($"EXCEPTION in {what}: {e.GetType().Name}: {e.Message}\n{e.StackTrace}"); }
    }

    // ================================================================ 1. rows

    static List<StudioAnime> PickRows(StudioDb db)
    {
        Section("anime groups with non-H rows (visible)");
        foreach (var g in db.AnimeGroups())
        {
            int nonH = 0, h = 0, vanillaNonH = 0;
            var cats = new List<string>();
            foreach (var c in db.CategoriesOf(g))
            {
                var rs = db.AnimeIn(c).ToList();
                int n = rs.Count(r => !r.IsH);
                nonH += n; h += rs.Count(r => r.IsH); vanillaNonH += rs.Count(r => !r.IsH && r.IsVanilla);
                if (n > 0) cats.Add($"{c.Number}:{c.Name}({n})");
            }
            if (nonH > 0) Line($"group {g.Number} \"{g.Name}\": non-H {nonH} (vanilla {vanillaNonH}), H {h}; categories {string.Join(" ", cats.Take(20))}{(cats.Count > 20 ? " ..." : "")}");
        }
        var group0 = new List<StudioAnime>();
        if (db.TryGetAnimeGroup(0, out var g0))
            foreach (var c in db.CategoriesOf(g0))
                group0.AddRange(db.AnimeIn(c).Where(r => !r.IsH && r.IsVanilla));
        Section($"group 0 vanilla non-H rows ({group0.Count})");
        foreach (var r in group0) Line($"{r.Group}/{r.Category}/{r.No} \"{r.Name}\" {r.BundlePath}:{r.FileName}:{r.Clip} options {r.Options.Count}");

        Section("vanilla non-H rows per category (names; battle group 15 left out)");
        foreach (var g in db.AnimeGroups())
            foreach (var c in db.CategoriesOf(g))
            {
                if (g.Number == 15) continue;
                var rs = db.AnimeIn(c).Where(r => !r.IsH && r.IsVanilla).ToList();
                if (rs.Count > 0) Line($"{g.Number}/{c.Number} {c.Name}: {string.Join(" | ", rs.Select(r => r.No + ":" + r.Name + "=" + r.Clip + (r.Options.Count > 0 ? "(+" + r.Options.Count + " opt)" : "")))}");
            }

        var picked = new List<StudioAnime>();
        string spec = Arg("-rows", null);
        if (spec != null)
        {
            foreach (var s in spec.Split(','))
            {
                var p = s.Split('/').Select(int.Parse).ToArray();
                var r = db.FindAnime(p[0], p[1], p[2]);
                if (r != null && !r.IsH) picked.Add(r); else Line($"-rows {s}: not found or H (ignored)");
            }
        }
        else
        {
            // Defaults (from the dump above): group 0 has only the T-pose row, so the character rows of group 19
            // ("キャラ2") are used: 立ち1 (standing idle), 登場1 (entrance walk), 座り1 (floor sit).
            foreach (var k in new[] { (19, 0, 0), (19, 5, 0), (19, 1, 20) })
            {
                var r = db.FindAnime(k.Item1, k.Item2, k.Item3);
                if (r != null && !r.IsH) picked.Add(r); else Line($"default row {k}: not found or H");
            }
        }
        Section("picked rows");
        foreach (var r in picked) Line($"{Label(r, picked)}: {r}");
        return picked;
    }

    static readonly string[] Labels = { "stand", "walk", "sit" };
    static string Label(StudioAnime r, List<StudioAnime> rows) { int i = rows.IndexOf(r); return i >= 0 && i < Labels.Length ? Labels[i] : "row" + i; }

    static void Controllers(ContentCatalog gd, List<StudioAnime> rows, Dictionary<StudioAnime, RuntimeAnimatorController> ctrls)
    {
        Section("1. controller load through the catalog");
        foreach (var r in rows)
        {
            var m = LogMark();
            var sw = Stopwatch.StartNew();
            var ctrl = gd.LoadAsset<RuntimeAnimatorController>(r);
            double ms = sw.Elapsed.TotalMilliseconds;
            Line($"{r.BundlePath}:{r.FileName} -> {(ctrl ? ctrl.GetType().Name + " \"" + ctrl.name + "\"" : "NULL")} in {ms:F1} ms; {LogSince(m)}");
            if (!ctrl) continue;
            ctrls[r] = ctrl;
            var clips = ctrl.animationClips;
            var distinct = clips.Distinct().ToList();
            Line($"   clips {clips.Length} ({distinct.Count} distinct); clip named like the state \"{r.Clip}\": {distinct.Any(c => c.name == r.Clip)}");
            foreach (var c in distinct.Take(12))
            {
                int bindings = -1, objBindings = -1;
                try { bindings = AnimationUtility.GetCurveBindings(c).Length; objBindings = AnimationUtility.GetObjectReferenceCurveBindings(c).Length; } catch { }
                Line($"   clip \"{c.name}\": length {c.length:F3} s, {c.frameRate} fps, loop {c.isLooping}, wrap {c.wrapMode}, legacy {c.legacy}, human {c.humanMotion}, " +
                     $"events {c.events.Length}, genericRoot {c.hasGenericRootTransform}, motionCurves {c.hasMotionCurves}, rootCurves {c.hasRootCurves}, motionFloat {c.hasMotionFloatCurves}, " +
                     $"editor curve bindings {bindings} (object {objBindings}), bounds {c.localBounds.size:F2}");
            }
            if (distinct.Count > 12) Line($"   ... {distinct.Count - 12} more clips");
        }
    }

    // ================================================================ 2-5. card

    struct Trs { public Vector3 P; public Quaternion R; public Vector3 S; }

    static Dictionary<Transform, Trs> Snap(Transform root)
    {
        var d = new Dictionary<Transform, Trs>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) d[t] = new Trs { P = t.localPosition, R = t.localRotation, S = t.localScale };
        return d;
    }

    static void Restore(Dictionary<Transform, Trs> s)
    {
        foreach (var kv in s) if (kv.Key) { kv.Key.localPosition = kv.Value.P; kv.Key.localRotation = kv.Value.R; kv.Key.localScale = kv.Value.S; }
    }

    static bool Moved(Transform t, Trs r) =>
        (t.localPosition - r.P).sqrMagnitude > 1e-10f || Quaternion.Angle(t.localRotation, r.R) > 0.01f || (t.localScale - r.S).sqrMagnitude > 1e-10f;

    static List<Transform> MovedSet(Dictionary<Transform, Trs> rest) => rest.Where(kv => kv.Key && Moved(kv.Key, kv.Value)).Select(kv => kv.Key).ToList();

    static string MovedSummary(Dictionary<Transform, Trs> rest, Transform headBone)
    {
        var m = MovedSet(rest);
        var shape = m.Where(t => t.name.EndsWith("_s")).ToList();
        var dam = m.Where(t => t.name.Contains("_dam")).ToList();
        var inHead = m.Where(t => headBone && t.IsChildOf(headBone)).ToList();
        var pos = m.Where(t => (t.localPosition - rest[t].P).sqrMagnitude > 1e-10f).ToList();
        var scl = m.Where(t => (t.localScale - rest[t].S).sqrMagnitude > 1e-10f).ToList();
        return $"moved {m.Count}/{rest.Count} (position {pos.Count}, scale {scl.Count}; *_s {shape.Count}{(shape.Count > 0 ? " [" + string.Join(",", shape.Take(12).Select(t => t.name)) + "]" : "")}; " +
               $"*_dam {dam.Count}; under p_cf_head_bone {inHead.Count}{(inHead.Count > 0 ? " [" + string.Join(",", inHead.Take(6).Select(t => t.name)) + "]" : "")}; " +
               $"position movers [{string.Join(",", pos.Take(8).Select(t => t.name))}{(pos.Count > 8 ? ",..." : "")}])";
    }

    static string Q(Quaternion q) { var e = q.eulerAngles; return $"({Mathf.DeltaAngle(0, e.x):F1},{Mathf.DeltaAngle(0, e.y):F1},{Mathf.DeltaAngle(0, e.z):F1})"; }

    static void CardSpike(ContentCatalog gd, StudioDb db, string path, List<StudioAnime> rows, Dictionary<StudioAnime, RuntimeAnimatorController> ctrls, bool render)
    {
        Section("2. BR-Chan headless");
        if (!File.Exists(path)) { Line("card missing: " + path); return; }
        var asm = new CharaAssembler(gd);
        var swB = Stopwatch.StartNew();
        var chara = asm.BuildFromDocuments(path);
        Line($"build {swB.ElapsedMilliseconds} ms");
        try
        {
            var ctx = asm.Context;
            ctx.Motion.AutoStep = false;
            var animator = ctx.Anim.GetComponent<Animator>();
            if (!animator) { Line("p_cf_anim has no Animator"); return; }
            var av = animator.avatar;
            Line($"prefab Animator: enabled {animator.enabled} (assembler), controller {(animator.runtimeAnimatorController ? animator.runtimeAnimatorController.name : "none")}, " +
                 $"avatar {(av ? av.name + " valid " + av.isValid + " human " + av.isHuman : "none")}, culling {animator.cullingMode}, update {animator.updateMode}, " +
                 $"applyRootMotion {animator.applyRootMotion}, keepStateOnDisable {animator.keepAnimatorStateOnDisable}, writeDefaultsOnDisable {animator.writeDefaultValuesOnDisable}, " +
                 $"stabilizeFeet {animator.stabilizeFeet}, layersAffectMassCenter {animator.layersAffectMassCenter}, logWarnings {animator.logWarnings}, fireEvents {animator.fireEvents}");
            var B = ctx.BodyBones;
            Transform Bone(string n) => B.TryGetValue(n, out var t) ? t : null;
            var arm = Bone("cf_J_ArmUp00_L"); var spine = Bone("cf_J_Spine01"); var headJ = Bone("cf_J_Head"); var hips = Bone("cf_J_Hips"); var jroot = Bone("cf_J_Root");
            var rest = Snap(ctx.Anim.transform);
            Line($"transforms under p_cf_anim: {rest.Count}; rest ArmUp00_L {Q(arm.localRotation)}, Spine01 {Q(spine.localRotation)}, Head {Q(headJ.localRotation)}, Hips pos {hips.localPosition:F4}, cf_J_Root pos {jroot.localPosition:F4}");
            var cullDefault = animator.cullingMode;

            var first = rows.FirstOrDefault(r => ctrls.ContainsKey(r));
            if (db.FindAnime(0, 0, 0) is StudioAnime tpose && !tpose.IsH)
            {
                var tc = gd.LoadAsset<RuntimeAnimatorController>(tpose);
                if (tc)
                {
                    animator.runtimeAnimatorController = tc;
                    animator.Play(tpose.Clip, 0, 0f); animator.Update(0f);
                    Line($"group 0 row 0/0/0 \"{tpose.Name}\" (state {tpose.Clip}): {MovedSummary(rest, ctx.HeadBone.transform)}; max rotation from rest {rest.Where(kv => kv.Key).Max(kv => Quaternion.Angle(kv.Key.localRotation, kv.Value.R)):F2} deg");
                    Restore(rest);
                    animator.runtimeAnimatorController = null;
                }
            }
            if (first == null) { Line("no controller loaded"); return; }

            // --- switches: disabled Animator, default culling, AlwaysAnimate
            Section("2a. Animator switches (row " + Label(first, rows) + ")");
            {
                var m = LogMark();
                animator.enabled = false;
                animator.runtimeAnimatorController = ctrls[first];
                animator.Play(first.Clip, 0, 0.25f);
                for (int i = 0; i < 10; i++) animator.Update(Dt);
                Line($"enabled=false, controller set, Play + 10x Update: initialized {animator.isInitialized}, {MovedSummary(rest, ctx.HeadBone.transform)}; {LogSince(m)}");
                Restore(rest);
                m = LogMark();
                animator.enabled = true;
                animator.cullingMode = cullDefault;
                Line($"enabled=true: initialized {animator.isInitialized}, isActiveAndEnabled {animator.isActiveAndEnabled}, hasBoundPlayables {animator.hasBoundPlayables}");
                animator.Play(first.Clip, 0, 0.25f);
                animator.Update(0f);
                Line($"enabled=true, culling {cullDefault}: Play(t 0.25) + Update(0): {MovedSummary(rest, ctx.HeadBone.transform)}; {LogSince(m)}");
                animator.Update(Dt);
                Line($"   + Update(1/60): ArmUp00_L {Q(arm.localRotation)}, normalized {animator.GetCurrentAnimatorStateInfo(0).normalizedTime:F4}");
                foreach (var cm in new[] { AnimatorCullingMode.CullUpdateTransforms, AnimatorCullingMode.CullCompletely, AnimatorCullingMode.AlwaysAnimate })
                {
                    Restore(rest);
                    animator.Rebind();
                    animator.cullingMode = cm;
                    m = LogMark();
                    animator.Play(first.Clip, 0, 0.25f);
                    animator.Update(0f);
                    int a = MovedSet(rest).Count;
                    for (int i = 0; i < 10; i++) animator.Update(Dt);
                    Line($"culling {cm}: Play(t 0.25) + Update(0) moved {a} bones; + 10 x Update(1/60) {MovedSummary(rest, ctx.HeadBone.transform)}, normalized {animator.GetCurrentAnimatorStateInfo(0).normalizedTime:F4}; {LogSince(m)}");
                }
                animator.cullingMode = cullDefault;
                // Inactive GameObject: does a manual Update still pose?
                Restore(rest);
                ctx.Anim.SetActive(false);
                m = LogMark();
                animator.Play(first.Clip, 0, 0.5f);
                animator.Update(0f);
                int inactiveMoved = MovedSet(rest).Count;
                ctx.Anim.SetActive(true);
                Line($"p_cf_anim inactive: Play + Update(0) moved {inactiveMoved} bones; {LogSince(m)}");
                // Disabled Animator with an animated row, and the state it keeps when re-enabled.
                Restore(rest);
                animator.enabled = false;
                animator.Play(first.Clip, 0, 0.5f);
                animator.Update(0f);
                int disabledMoved = MovedSet(rest).Count;
                float tDisabled = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                animator.enabled = true;
                float tReenabled = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                Line($"enabled=false with row {Label(first, rows)}: Play(t 0.5) + Update(0) moved {disabledMoved} bones, normalized {tDisabled:F4}; after enabled=true normalized {tReenabled:F4} (keepAnimatorStateOnDisable {animator.keepAnimatorStateOnDisable})");
                // Bones written by hand between updates: does the Animator overwrite them every Update (also at dt 0)?
                Restore(rest);
                animator.Play(first.Clip, 0, 0.5f); animator.Update(0f);
                var armBone = Bone("cf_J_ArmUp00_L"); var qa = armBone.localRotation;
                armBone.localRotation = Quaternion.Euler(0, 0, 45);
                animator.Update(0f);
                Line($"hand write to ArmUp00_L then Update(0): restored to clip value {Quaternion.Angle(armBone.localRotation, qa) < 0.01f} (diff {Quaternion.Angle(armBone.localRotation, qa):F3} deg)");
                var sb = Bone("cf_J_Spine01_s") ?? B.Values.First(t => t.name.EndsWith("_s"));
                var ss = sb.localScale;
                sb.localScale = ss * 1.5f;
                animator.Update(0f);
                Line($"hand write to {sb.name} scale then Update(0): kept {(sb.localScale - ss * 1.5f).sqrMagnitude < 1e-10f}");
                sb.localScale = ss;
            }

            // --- per row
            foreach (var r in rows)
            {
                if (!ctrls.TryGetValue(r, out var ctrl)) continue;
                string label = Label(r, rows);
                Section($"2b. row {label}: {r.Name} ({r.Group}/{r.Category}/{r.No}, state \"{r.Clip}\")");
                Restore(rest);
                var m = LogMark();
                var sw = Stopwatch.StartNew();
                animator.runtimeAnimatorController = ctrl;
                double assignMs = sw.Elapsed.TotalMilliseconds;
                Line($"assign controller {assignMs:F2} ms; initialized {animator.isInitialized}; layers {animator.layerCount} [{string.Join(", ", Enumerable.Range(0, animator.layerCount).Select(i => animator.GetLayerName(i) + " w" + animator.GetLayerWeight(i).ToString("F2")))}]");
                var ps = animator.parameters;
                Line($"parameters {ps.Length}: {string.Join(", ", ps.Select(p => $"{p.name}:{p.type}={(p.type == AnimatorControllerParameterType.Float ? p.defaultFloat.ToString("F2") : p.type == AnimatorControllerParameterType.Int ? p.defaultInt.ToString() : p.defaultBool.ToString())}"))}");
                int shortHash = Animator.StringToHash(r.Clip), fullHash = Animator.StringToHash("Base Layer." + r.Clip);
                Line($"HasState(0, short \"{r.Clip}\") {animator.HasState(0, shortHash)}, HasState(0, \"Base Layer.{r.Clip}\") {animator.HasState(0, fullHash)}");
                animator.Play(r.Clip, 0, 0f);
                sw.Restart();
                animator.Update(0f);
                double firstMs = sw.Elapsed.TotalMilliseconds;
                var st = animator.GetCurrentAnimatorStateInfo(0);
                var ci = animator.GetCurrentAnimatorClipInfo(0);
                Line($"first Update(0) {firstMs:F2} ms; state shortHash match {st.shortNameHash == shortHash}, fullPath match {st.fullPathHash == fullHash}, length {st.length:F3} s, loop {st.loop}, speed {st.speed}, " +
                     $"speedMultiplier {st.speedMultiplier}, normalized {st.normalizedTime:F4}; clips [{string.Join(", ", ci.Select(c => c.clip.name + " w" + c.weight.ToString("F2")))}]; {LogSince(m)}");
                Line($"Update(0) at t 0: {MovedSummary(rest, ctx.HeadBone.transform)}");

                foreach (float t in new[] { 0f, 0.25f, 0.5f, 0.75f })
                {
                    animator.Play(r.Clip, 0, t);
                    animator.Update(0f);
                    var s2 = animator.GetCurrentAnimatorStateInfo(0);
                    Line($"t {t:F2} (read {s2.normalizedTime:F4}): ArmUp00_L {Q(arm.localRotation)} ({Quaternion.Angle(arm.localRotation, rest[arm].R):F1} deg from rest), " +
                         $"Spine01 {Q(spine.localRotation)} ({Quaternion.Angle(spine.localRotation, rest[spine].R):F1}), Head {Q(headJ.localRotation)} ({Quaternion.Angle(headJ.localRotation, rest[headJ].R):F1}), " +
                         $"Hips pos {hips.localPosition:F4}, cf_J_Root pos {jroot.localPosition:F4}, p_cf_anim pos {ctx.Anim.transform.localPosition:F4}");
                }

                // pause: speed 0, set the time, Update(0) re-poses and Update(dt) holds
                {
                    animator.Play(r.Clip, 0, 0.5f); animator.Update(0f);
                    var q05 = arm.localRotation;
                    animator.Play(r.Clip, 0, 0f); animator.Update(0f);
                    animator.speed = 0f;
                    animator.Play(r.Clip, 0, 0.5f); animator.Update(0f);
                    float d1 = Quaternion.Angle(arm.localRotation, q05);
                    for (int i = 0; i < 30; i++) animator.Update(Dt);
                    float d2 = Quaternion.Angle(arm.localRotation, q05);
                    float tn = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                    animator.speed = 2f;
                    animator.Play(r.Clip, 0, 0f); animator.Update(0f);
                    for (int i = 0; i < 30; i++) animator.Update(Dt);
                    float t2 = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                    animator.speed = 1f;
                    Line($"speed 0: Play(t 0.5)+Update(0) matches the t 0.5 pose {d1:F4} deg, after 30 x Update(1/60) {d2:F4} deg, normalized {tn:F4}; speed 2: 30 frames -> normalized {t2:F4} (state length {animator.GetCurrentAnimatorStateInfo(0).length:F3}, state speed {animator.GetCurrentAnimatorStateInfo(0).speed})");
                }
                // root motion
                foreach (bool arm2 in new[] { false, true })
                {
                    Restore(rest);
                    animator.applyRootMotion = arm2;
                    animator.Play(r.Clip, 0, 0f);
                    animator.Update(0f);
                    var p0 = ctx.Anim.transform.position; var h0 = hips.position; var j0 = jroot.localPosition;
                    var dp = Vector3.zero;
                    for (int i = 0; i < 120; i++) { animator.Update(Dt); dp += animator.deltaPosition; }
                    var s3 = animator.GetCurrentAnimatorStateInfo(0);
                    var qEnd = arm.localRotation; animator.Update(Dt);
                    if (!arm2) Line($"after 120 frames: normalized {animator.GetCurrentAnimatorStateInfo(0).normalizedTime:F4}, loop {s3.loop}, pose still changing {Quaternion.Angle(qEnd, arm.localRotation) > 0.001f}");
                    Line($"applyRootMotion {arm2}: 120 x Update(1/60) -> normalized {s3.normalizedTime:F4} (expected {2f / Mathf.Max(s3.length, 1e-6f):F4}), p_cf_anim moved {(ctx.Anim.transform.position - p0).magnitude:F4}, " +
                         $"Hips world moved {(hips.position - h0).magnitude:F4} ({hips.position - h0:F3}), cf_J_Root local moved {(jroot.localPosition - j0).magnitude:F4}, sum deltaPosition {dp:F4}, velocity {animator.velocity:F3}");
                }
                animator.applyRootMotion = false;
                ctx.Anim.transform.localPosition = Vector3.zero; ctx.Anim.transform.localRotation = Quaternion.identity;

                // per-frame timing
                Restore(rest);
                animator.Play(r.Clip, 0, 0f); animator.Update(0f);
                sw.Restart();
                const int N = 600;
                for (int i = 0; i < N; i++) animator.Update(Dt);
                Line($"timing: {N} x Update(1/60) {sw.Elapsed.TotalMilliseconds:F1} ms = {sw.Elapsed.TotalMilliseconds * 1000 / N:F1} us/frame");
            }

            // --- 3. interplay
            var walk = rows.Count > 1 && ctrls.ContainsKey(rows[1]) ? rows[1] : first;
            Guard("interplay", () => Interplay(ctx, animator, rest, walk, ctrls[walk], Label(walk, rows)));

            // --- 7. PlayableGraph path
            Guard("playable", () => PlayableSpike(ctx, animator, rest, rows[0], ctrls[rows[0]]));

            // --- 5. mod
            Guard("mod", () => ModSpike(gd, db, ctx, animator, rest));

            // --- 4. render
            if (render) Guard("render", () => Render(chara, ctx, animator, rest, rows, ctrls));
        }
        finally { Object.DestroyImmediate(chara); }
    }

    static bool AnyNaN(Transform root)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var p = t.localPosition; var q = t.localRotation;
            if (float.IsNaN(p.x + p.y + p.z) || float.IsNaN(q.x + q.y + q.z + q.w) || float.IsInfinity(p.x + p.y + p.z)) return true;
        }
        return false;
    }

    static void Interplay(CharaContext ctx, Animator animator, Dictionary<Transform, Trs> rest, StudioAnime row, RuntimeAnimatorController ctrl, string label)
    {
        Section($"3. interplay with the motion stages (row {label}, 120 frames, Animator.Update before Motion.Step)");
        var motion = ctx.Motion;
        Line("stages: " + string.Join(" < ", motion.Stages.Select(s => s.Name + ":" + (int)s.Phase)));
        var look = CharaLook.Of(ctx.Root);
        var corr = CharaJointCorrection.Of(ctx.Root);
        var dyn = motion.Get<DynamicBoneStage>();
        var B = ctx.BodyBones;
        var headJ = B["cf_J_Head"]; var arm = B["cf_J_ArmUp00_L"]; var dam = B.TryGetValue("cf_J_ArmUp01_dam_L", out var d1) ? d1 : null;
        var target = new GameObject("anim_spike_target").transform;
        target.SetParent(ctx.Root.transform, false);
        target.position = headJ.position + new Vector3(-25, 0, 15);
        LookTargets.DefaultCamera = target;
        animator.runtimeAnimatorController = ctrl;
        if (corr != null) corr[0] = true;

        Quaternion RunPass(int neckPtn, out string info)
        {
            Restore(rest);
            animator.Rebind();
            motion.ResetAll();
            look?.SetNeckPattern(neckPtn);
            animator.Play(row.Clip, 0, 0f);
            animator.Update(0f);
            int err0 = motion.Errors; bool nan = false;
            float worstHair = 0f, worstHairRatio = 0f; int hairChains = 0;
            float damRefresh = 0f; var damSeen = new List<float>(); var armSeen = new List<float>();
            var m = LogMark();
            for (int f = 0; f < 120; f++)
            {
                animator.Update(Dt);
                motion.Step(Dt);
                if (!nan && AnyNaN(ctx.Root.transform)) nan = true;
                if (dyn != null)
                {
                    hairChains = 0;
                    foreach (var c in dyn.Chains)
                    {
                        if (c.Category != DynamicBoneCategory.Hair || !c.Active || c.ParticleCount < 2 || !c.Root) continue;
                        hairChains++;
                        float maxD = 0f;
                        for (int i = 0; i < c.ParticleCount; i++) maxD = Mathf.Max(maxD, (c.Position(i) - c.Root.position).magnitude);
                        worstHair = Mathf.Max(worstHair, maxD);
                        if (c.TotalLength > 1e-4f) worstHairRatio = Mathf.Max(worstHairRatio, maxD / c.TotalLength);
                    }
                }
                if (dam != null && corr != null && (f % 30 == 29))
                {
                    var q = dam.localRotation;
                    corr.Refresh();
                    damRefresh = Mathf.Max(damRefresh, Quaternion.Angle(q, dam.localRotation));
                    damSeen.Add(Quaternion.Angle(dam.localRotation, rest[dam].R));
                    armSeen.Add(Quaternion.Angle(arm.localRotation, rest[arm].R));
                }
            }
            info = $"neck pattern {neckPtn}: motion errors {motion.Errors - err0}, NaN {nan}, {LogSince(m)}; hair chains {hairChains}, max particle distance from chain root {worstHair:F3} (max ratio to chain length {worstHairRatio:F2}); " +
                   $"dam ArmUp01_dam_L vs re-evaluation on the final pose (frames 30/60/90/120) max {damRefresh:F4} deg; dam from rest [{string.Join(", ", damSeen.Select(x => x.ToString("F1")))}] deg while ArmUp00_L from rest [{string.Join(", ", armSeen.Select(x => x.ToString("F1")))}] deg; " +
                   $"normalized {animator.GetCurrentAnimatorStateInfo(0).normalizedTime:F3}";
            return headJ.rotation;
        }

        var qAnim = RunPass(3, out var iA);
        Line(iA);
        var animFwd = qAnim * Vector3.forward;
        var qLook = RunPass(1, out var iB);
        Line(iB);
        var lookFwd = qLook * Vector3.forward;
        var toTarget = (target.position - headJ.position).normalized;
        Line($"head world rotation, look vs animation: {Quaternion.Angle(qAnim, qLook):F2} deg; head forward to target: animation {Vector3.Angle(animFwd, toTarget):F1} deg, look {Vector3.Angle(lookFwd, toTarget):F1} deg (look must be closer)");
        if (corr != null) corr[0] = false;
        look?.SetNeckPattern(3);
        LookTargets.DefaultCamera = null;
        Object.DestroyImmediate(target.gameObject);
    }

    static void ModSpike(ContentCatalog gd, StudioDb db, CharaContext ctx, Animator animator, Dictionary<Transform, Trs> rest)
    {
        Section("5. mod controller");
        StudioAnime pick = null; int seen = 0, vanillaBundle = 0;
        foreach (var g in db.AnimeGroups())
            foreach (var c in db.CategoriesOf(g))
                foreach (var r in db.AnimeIn(c))
                {
                    if (r.IsH || r.IsVanilla) continue;
                    seen++;
                    if (gd.HasRealBundle(r.BundlePath)) { vanillaBundle++; continue; }
                    if (pick == null) pick = r;
                }
        Line($"non-H mod rows visible {seen}, of which with a vanilla (abdata) controller bundle {vanillaBundle}");
        if (pick == null) { Line("no non-H mod row with a zipmod controller bundle"); return; }
        var ship = pick.Zipmod != null && pick.Zipmod.HasBundle(pick.BundlePath) ? pick.Zipmod : gd.FindZipmodShipping(pick.BundlePath);
        Line($"row: {pick} (zipmod {pick.Zipmod?.Guid}; bundle shipped by {ship?.Guid ?? "none"})");
        var m = LogMark();
        var sw = Stopwatch.StartNew();
        var ctrl = gd.LoadAsset<RuntimeAnimatorController>(pick);
        double ms = sw.Elapsed.TotalMilliseconds;
        Line($"load {ms:F1} ms -> {(ctrl ? ctrl.GetType().Name + " \"" + ctrl.name + "\", clips " + ctrl.animationClips.Length : "NULL")}; {LogSince(m)}");
        if (!ctrl) return;
        Restore(rest);
        animator.runtimeAnimatorController = ctrl;
        bool has = animator.HasState(0, Animator.StringToHash(pick.Clip));
        animator.Play(pick.Clip, 0, 0.5f);
        sw.Restart();
        animator.Update(0f);
        double first = sw.Elapsed.TotalMilliseconds;
        var st = animator.GetCurrentAnimatorStateInfo(0);
        Line($"HasState {has}, first Update {first:F2} ms, state match {st.shortNameHash == Animator.StringToHash(pick.Clip)}, length {st.length:F3}, loop {st.loop}, normalized {st.normalizedTime:F3}, " +
             $"params {animator.parameters.Length} [{string.Join(", ", animator.parameters.Select(p => p.name))}]; {MovedSummary(rest, ctx.HeadBone.transform)}; {LogSince(m)}");
        Restore(rest);
    }

    // ================================================================ 7. playable graph

    static void PlayableSpike(CharaContext ctx, Animator animator, Dictionary<Transform, Trs> rest, StudioAnime row, RuntimeAnimatorController ctrl)
    {
        Section($"7. PlayableGraph path (AnimatorControllerPlayable -> AnimationPlayableOutput, manual), row {row.Name} state {row.Clip}");
        var arm = ctx.BodyBones["cf_J_ArmUp00_L"];
        var expA05 = new Quaternion(0.40005f, 0.56880f, 0.41084f, 0.58960f);
        var expA0 = new Quaternion(0.36787f, 0.57102f, 0.42623f, 0.59744f);
        int hash = Animator.StringToHash(row.Clip);
        foreach (bool enabled in new[] { false, true })
        {
            Restore(rest);
            animator.runtimeAnimatorController = null;
            animator.enabled = enabled;
            animator.applyRootMotion = false;
            var m = LogMark();
            var sw = Stopwatch.StartNew();
            var graph = UnityEngine.Playables.PlayableGraph.Create("anim_spike");
            graph.SetTimeUpdateMode(UnityEngine.Playables.DirectorUpdateMode.Manual);
            var cp = UnityEngine.Animations.AnimatorControllerPlayable.Create(graph, ctrl);
            var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "body", animator);
            output.SetSourcePlayable(cp);
            double createMs = sw.Elapsed.TotalMilliseconds;
            try
            {
                cp.Play(hash, 0, 0.5f);
                sw.Restart();
                graph.Evaluate(0f);
                double firstMs = sw.Elapsed.TotalMilliseconds;
                var st = cp.GetCurrentAnimatorStateInfo(0);
                Line($"Animator.enabled {enabled}: create {createMs:F2} ms, first Evaluate(0) {firstMs:F2} ms; Play(t 0.5)+Evaluate(0): state match {st.shortNameHash == hash}, normalized {st.normalizedTime:F4}, " +
                     $"ArmUp00_L vs example A t 0.5 {Quaternion.Angle(arm.localRotation, expA05):F4} deg; {MovedSummary(rest, ctx.HeadBone.transform)}; params {cp.GetParameterCount()}; {LogSince(m)}");
                cp.Play(hash, 0, 0f); graph.Evaluate(0f);
                Line($"   Play(t 0)+Evaluate(0): normalized {cp.GetCurrentAnimatorStateInfo(0).normalizedTime:F4}, ArmUp00_L vs example A t 0 {Quaternion.Angle(arm.localRotation, expA0):F4} deg");
                for (int i = 0; i < 80; i++) graph.Evaluate(Dt);
                Line($"   80 x Evaluate(1/60): normalized {cp.GetCurrentAnimatorStateInfo(0).normalizedTime:F5} (example A: 0.5), ArmUp00_L vs t 0.5 {Quaternion.Angle(arm.localRotation, expA05):F4} deg");
                // speed 0: set time and Evaluate(0)
                cp.Play(hash, 0, 0.5f); graph.Evaluate(0f);
                for (int i = 0; i < 30; i++) graph.Evaluate(0f);
                Line($"   Evaluate(0) x 30 after Play(0.5): normalized {cp.GetCurrentAnimatorStateInfo(0).normalizedTime:F4}, held {Quaternion.Angle(arm.localRotation, expA05):F4} deg");
                // hand write between evaluations
                arm.localRotation = Quaternion.identity; graph.Evaluate(0f);
                Line($"   hand write to ArmUp00_L then Evaluate(0): restored {Quaternion.Angle(arm.localRotation, expA05):F4} deg");
                // a queued Play read back before evaluation
                cp.Play(hash, 0, 0.25f);
                Line($"   right after Play(0.25) without evaluation: normalized reads {cp.GetCurrentAnimatorStateInfo(0).normalizedTime:F4}");
                graph.Evaluate(0f);
                sw.Restart();
                for (int i = 0; i < 600; i++) graph.Evaluate(Dt);
                Line($"   timing: 600 x Evaluate(1/60) {sw.Elapsed.TotalMilliseconds:F1} ms = {sw.Elapsed.TotalMilliseconds * 1000 / 600:F1} us/frame; p_cf_anim pos {ctx.Anim.transform.localPosition:F4}");
            }
            finally { graph.Destroy(); }
        }
        animator.enabled = false;
        animator.applyRootMotion = true;
        Restore(rest);
    }

    // ================================================================ 4. render

    static void Render(GameObject chara, CharaContext ctx, Animator animator, Dictionary<Transform, Trs> rest, List<StudioAnime> rows, Dictionary<StudioAnime, RuntimeAnimatorController> ctrls)
    {
        Section("4. render (BR-Chan, URP, full body)");
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Line("needs a graphics device (run without -nographics)"); return; }
        ShaderUtil.allowAsyncCompilation = false;
        var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HS2Next.App.AppPipeline.MeasureAssetPath);
        HS2Next.ShaderMap.ApplyUrp(chara);
        foreach (var s in chara.GetComponentsInChildren<SkinnedMeshRenderer>(true)) s.forceMatrixRecalculationPerRender = true;
        var expr = HS2Next.Motion.Face.CharaExpression.Of(chara);
        if (expr != null) expr.Blink = false;
        try
        {
            for (int i = 0; i < Math.Min(rows.Count, 2); i++)
            {
                var r = rows[i];
                if (!ctrls.TryGetValue(r, out var ctrl)) continue;
                foreach (float t in new[] { 0f, 0.5f })
                {
                    Restore(rest);
                    animator.runtimeAnimatorController = ctrl;
                    ctx.Motion.ResetAll();
                    // Re-pose each frame at the same time so the hair settles on the held pose.
                    for (int f = 0; f < 60; f++) { animator.Play(r.Clip, 0, t); animator.Update(0f); ctx.Motion.Step(Dt); }
                    string name = $"anim_spike_{Label(r, rows)}_{(t == 0 ? "0" : "05")}";
                    string prefix = Path.Combine(OutDir, name);
                    CharaRenderTest.RenderViews(chara, prefix, asset, "full");
                    string file = prefix + ".png";
                    if (File.Exists(file)) File.Delete(file);
                    File.Move(prefix + "_full.png", file);
                    Line($"{name}.png: {r.Name} t {t}, ArmUp00_L {Q(ctx.BodyBones["cf_J_ArmUp00_L"].localRotation)}, Hips {ctx.BodyBones["cf_J_Hips"].localPosition:F3}");
                }
            }
        }
        finally { GraphicsSettings.defaultRenderPipeline = null; QualitySettings.renderPipeline = null; }
    }
}
