// SceneRuntimeTest.RunPlay: the play-mode part of the scene runtime tests, run
// with graphics through Editor/PlayModeHarness.cs and `Tools/unity_batch.ps1 -NoQuit`:
//   & Tools\unity_batch.ps1 -Method SceneRuntimeTest.RunPlay -NoQuit -LogName scene_play
// The entry stores the coroutine in SessionState and enters play mode in a new empty scene; the harness runs
// PlayMain from EditorApplication.update and exits with its result. The scene runtime runs with its play-mode
// drivers (Apply every Update, LateApply every LateUpdate) and the parts of Package C (map, view camera, chara light).
//   D1  play mode entered from -executeMethod in -batchmode without the Test Framework; the player loop advances
//   L1-L3  MapRealization without a session: cold load and clear; a superseded load in flight is discarded; the same
//       map re-requested while its first load is in flight (one scene, the shared bundle kept until Clear)
//   P1  SetMap(illusion.hs2#10): Loaded within 120 s, map scene active, >= 1 root, no Built-in shader, no enabled
//       camera or audio listener inside it
//   P2  map pose (0, -1, 0) / (0, 90, 0) in one transaction: every root at R·p0 + T ± 1e-4; undo → identity ± 1e-4
//   P3  SetMap(null): scene unloaded, roots destroyed, runtime scene active, bundle unloaded, scene nodes intact
//   P4  BR-Chan from card with item B (illusion.hs2#399) at N_Hand_R, Idle_00 with AutoStep for 60 frames on the map;
//       save S1 → new session and runtime with ManualStepping → load → realize → wait for the map → save S2: S2 = S1
// Characters: the SFW female visual target of Tools/targets.json; the vanilla map 10 only.
// Report: Spike/out/scene/scene_runtime_play_report.txt, first line "N PASS, M FAIL"; exit 1 on any FAIL.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

public static partial class SceneRuntimeTest
{
    static string PlayReportPath => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "scene", "scene_runtime_play_report.txt");

    /// <summary>Batch entry (needs -NoQuit): enters play mode and runs <see cref="PlayMain"/>.</summary>
    public static void RunPlay()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PlayReportPath));
        File.WriteAllText(PlayReportPath, "0 PASS, 1 FAIL\nSceneRuntimeTest.play: started, no result yet\n", new UTF8Encoding(false));
        PlayModeHarness.Start(nameof(SceneRuntimeTest) + "." + nameof(PlayMain), PlayReportPath, 20);
    }

    static IEnumerator PlayMain() => PlayCase.Main();

    static class PlayCase
    {
        static readonly StringBuilder body = new StringBuilder();
        static int pass, fail;
        static readonly string MapRef = ContentId.Vanilla(10).ToString();
        static readonly DateTime FixedClock = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

        static void Check(string name, bool ok, string detail = "")
        {
            if (ok) pass++; else fail++;
            body.AppendLine($"{(ok ? "PASS" : "FAIL")} {name}{(string.IsNullOrEmpty(detail) ? "" : "  (" + detail + ")")}");
        }

        static void Line(string s) => body.AppendLine(s);

        static SaveOptions SaveOpts() => new SaveOptions { Thumbnail = ThumbnailMode.Placeholder, Clock = () => FixedClock };

        static void AddParts(SceneRuntime r)
        {
            r.AddStandardParts();   // map, view camera, chara light, in that order
        }

        public static IEnumerator Main()
        {
            var total = Stopwatch.StartNew();
            Line($"play mode: entered {PlayModeHarness.EnterSeconds:F1} s after the request; batch {Application.isBatchMode}, " +
                 $"graphics {SystemInfo.graphicsDeviceType}, Unity {Application.unityVersion}");
            Check("D1: play mode entered from -executeMethod in -batchmode (no Test Framework)", EditorApplication.isPlaying && Application.isPlaying);
            int f0 = Time.frameCount;
            var fw = Stopwatch.StartNew();
            yield return PlayModeHarness.WaitFrames(5);
            Check("D1: the player loop advances", Time.frameCount - f0 >= 5, $"frameCount {f0} -> {Time.frameCount} in {fw.ElapsedMilliseconds} ms");

            RuntimeServices services = null;
            var sw = Stopwatch.StartNew();
            try
            {
                var gd = CardResolveTest.BuildDb(null);
                services = new RuntimeServices { Catalog = gd, Db = gd.LoadStudioLists(), Hs2Root = CardResolveTest.GameRoot };
            }
            catch (Exception e) { Check("catalog and Studio lists", false, e.ToString()); }
            Line($"catalog + Studio lists: {sw.ElapsedMilliseconds} ms");
            if (services != null)
            {
                yield return Guarded("standalone map loader (L1-L3)", Standalone(services));
                yield return Guarded("session play test (P1-P4)", Run(services));
                try { services.Catalog.UnloadAll(); } catch (Exception e) { Line("catalog unload threw: " + e.Message); }
            }

            Line($"total {total.ElapsedMilliseconds} ms");
            Write();
            PlayModeHarness.ExitCode = fail > 0 ? 1 : 0;
        }

        /// <summary>Runs <paramref name="steps"/>; an exception becomes one FAIL line and ends that part only.</summary>
        static IEnumerator Guarded(string what, IEnumerator steps)
        {
            while (true)
            {
                object cur;
                try
                {
                    if (!steps.MoveNext()) yield break;
                    cur = steps.Current;
                }
                catch (Exception e) { Check(what + " ran without an exception", false, e.ToString()); yield break; }
                yield return cur;
            }
        }

        // L1-L3: MapRealization without a session (the loader alone).
        static IEnumerator Standalone(RuntimeServices services)
        {
            var findings = new SceneFindings();
            var map = new MapRealization(services.Catalog, services.Db, findings);
            var app = SceneManager.GetActiveScene();
            int scenes0 = SceneManager.sceneCount;
            var sw = Stopwatch.StartNew();
            map.SetTarget(MapRef, Vec3.Zero, Vec3.Zero);
            bool[] ok = { false };
            yield return PlayModeHarness.WaitUntil(() => { map.Apply(); return !map.IsBusy; }, 120, ok);
            Line($"L1 cold load of {MapRef} ({map.Row?.Name}, {map.Row?.BundlePath}, scene {map.Row?.FileName}): {map.State} after {sw.ElapsedMilliseconds} ms " +
                 $"(job {map.LoadMs} ms, bundle open {map.BundleMs} ms), roots {map.Roots.Count}, mapped materials {map.MappedMaterials}, " +
                 $"disabled cameras {map.DisabledCameras}, listeners {map.DisabledListeners}, static-batched renderers {map.StaticBatchedRenderers}");
            Check("L1: standalone load reaches Loaded and makes the map scene active", ok[0] && map.State == MapState.Loaded && SceneManager.GetActiveScene() == map.Scene,
                  $"{map.State}; findings {Codes(findings)}");
            var bundle = map.Bundle;
            string sceneName = map.Row?.FileName;
            map.SetTarget(null, Vec3.Zero, Vec3.Zero);
            yield return PlayModeHarness.WaitUntil(() => { map.Apply(); return !map.IsBusy && map.PendingUnloads == 0; }, 60, ok);
            Check("L1: clear unloads scene and bundle, app scene active", ok[0] && map.State == MapState.None && bundle == null &&
                  SceneManager.sceneCount == scenes0 && SceneManager.GetActiveScene() == app, $"scenes {SceneManager.sceneCount} (before {scenes0})");

            // L2: a load in flight is superseded: the arriving scene is unloaded at once
            sw.Restart();
            map.SetTarget(MapRef, Vec3.Zero, Vec3.Zero);
            map.Apply();
            bool started = map.State == MapState.Loading;
            int gen = map.Generation;
            map.SetTarget(null, Vec3.Zero, Vec3.Zero);
            map.Apply();
            yield return PlayModeHarness.WaitUntil(() => { map.Apply(); return SceneManager.sceneCount == scenes0 && map.PendingUnloads == 0; }, 120, ok);
            yield return PlayModeHarness.WaitFrames(2);
            map.Apply();
            bool bundleGone = !AssetBundle.GetAllLoadedAssetBundles().Any(b => b && b.GetAllScenePaths().Any(p => string.Equals(Path.GetFileNameWithoutExtension(p), sceneName, StringComparison.OrdinalIgnoreCase)));
            Check("L2: a superseded load is discarded (scene unloaded, bundle unloaded, state None)",
                  started && map.Generation > gen && ok[0] && map.State == MapState.None && SceneManager.GetActiveScene() == app && bundleGone,
                  $"started {started}, generation {gen} -> {map.Generation}, scenes {SceneManager.sceneCount}, {sw.ElapsedMilliseconds} ms, bundle gone {bundleGone}");

            // L3: map → none → the same map while the first load is in flight: the stale scene goes, the shared bundle stays
            sw.Restart();
            map.SetTarget(MapRef, Vec3.Zero, Vec3.Zero);
            map.Apply();
            map.SetTarget(null, Vec3.Zero, Vec3.Zero);
            map.Apply();
            map.SetTarget(MapRef, Vec3.Zero, Vec3.Zero);
            map.Apply();
            yield return PlayModeHarness.WaitUntil(() => { map.Apply(); return !map.IsBusy && map.PendingUnloads == 0; }, 120, ok);
            var shared = map.Bundle;
            Check("L3: re-request during a load: one map scene loaded, the shared bundle kept",
                  ok[0] && map.State == MapState.Loaded && SceneManager.sceneCount == scenes0 + 1 && shared != null && map.Roots.Count >= 1 && map.Roots.All(r => r != null),
                  $"{map.State}, scenes {SceneManager.sceneCount} (before {scenes0}), bundle {(shared != null ? "loaded" : "gone")}, {sw.ElapsedMilliseconds} ms");
            map.Clear();
            yield return PlayModeHarness.WaitUntil(() => { map.Apply(); return map.PendingUnloads == 0; }, 60, ok);
            Check("L3: Clear unloads scene and bundle", ok[0] && shared == null && SceneManager.sceneCount == scenes0 && SceneManager.GetActiveScene() == app);
        }

        static IEnumerator Run(RuntimeServices services)
        {
            string card = TestTargets.Load().VisualCards.FirstOrDefault();
            if (card == null || !File.Exists(card)) { Check("female visual target card exists", false, card ?? "none"); yield break; }

            // ---------------------------------------------------------------- session 1 and the fixture
            var session = new SceneSession(services.NewSessionOptions());
            var rt = new SceneRuntime(session, services);
            AddParts(rt);
            var map = rt.GetPart<MapRealization>();
            Check("runtime root outside any map scene, drivers on", rt.Root.scene == SceneManager.GetActiveScene() && rt.Root.GetComponent<SceneRuntimeDriver>() != null);

            var sw = Stopwatch.StartNew();
            var addA = rt.Ops.AddCharaFromCard(card);
            string bra = addA.NewId;
            var addB = bra != null ? rt.Ops.AddItem(ContentId.Vanilla(399), bra, "N_Hand_R") : null;
            string itemB = addB?.NewId;
            Check("fixture: add BR-Chan and item B", addA.Ok && addB != null && addB.Ok, $"{addA}, {addB}");
            yield return null;
            var braR = rt.FindChara(bra);
            Check("fixture: BR-Chan built, item B built at N_Hand_R", braR != null && braR.State == RealizationState.Built && rt.Find(itemB)?.State == RealizationState.Built,
                  $"{braR?.State}, item {rt.Find(itemB)?.State}, {sw.ElapsedMilliseconds} ms");
            if (braR == null || braR.State != RealizationState.Built) yield break;

            // ---------------------------------------------------------------- P1 map load
            var appScene = rt.Root.scene;
            sw.Restart();
            var res = session.SetMap(MapRef);
            bool[] ok = { false };
            yield return PlayModeHarness.WaitUntil(() => map.State != MapState.None && !map.IsBusy, 120, ok);
            long waited = sw.ElapsedMilliseconds;
            Line($"P1 map {MapRef} ({map.Row?.Name} {map.Row?.BundlePath} {map.Row?.FileName}): {map.State} after {waited} ms " +
                 $"(job {map.LoadMs} ms, bundle open {map.BundleMs} ms); scene {map.ScenePath}; roots {map.Roots.Count}; " +
                 $"mapped materials {map.MappedMaterials}; disabled cameras {map.DisabledCameras}, listeners {map.DisabledListeners}; " +
                 $"static-batched renderers {map.StaticBatchedRenderers}");
            Check("P1: SetMap ok and the map Loaded within 120 s", res.Ok && ok[0] && map.State == MapState.Loaded, $"{res}, {map.State}, {waited} ms");
            if (map.State != MapState.Loaded) { Line("findings: " + session.Findings.ToString().Replace("\n", "; ")); yield break; }
            Check("P1: map scene active", SceneManager.GetActiveScene() == map.Scene, SceneManager.GetActiveScene().name);
            Check("P1: at least one root", map.Roots.Count >= 1, $"{map.Roots.Count}");
            var builtIn = BuiltInMaterials(map.Roots);
            Check("P1: no Built-in shader on the map's renderers", builtIn.Count == 0, string.Join(", ", builtIn.Distinct().Take(8)));
            int cams = map.Roots.Sum(r => r.GetComponentsInChildren<Camera>(true).Count(c => c.enabled));
            int listeners = map.Roots.Sum(r => r.GetComponentsInChildren<AudioListener>(true).Count(c => c.enabled));
            Check("P1: no enabled camera or audio listener in the map scene", cams == 0 && listeners == 0, $"{cams} cameras, {listeners} listeners");
            Check("P1: the runtime root stayed in its own scene", rt.Root.scene == appScene);

            // ---------------------------------------------------------------- P2 map pose
            var roots = map.Roots.ToArray();
            var p0 = roots.Select(r => r.transform.position).ToArray();
            var q0 = roots.Select(r => r.transform.rotation).ToArray();
            CommandResult committed;
            using (var tx = session.Commands.Begin("Map pose"))
            {
                tx.Set(null, "map.pos", new Vec3(0, -1, 0));
                tx.Set(null, "map.rot", new Vec3(0, 90, 0));
                committed = tx.Commit();
            }
            yield return null;
            var R = Quaternion.Euler(0, 90, 0);
            var T = new Vector3(0, -1, 0);
            float posErr = 0f, rotErr = 0f;
            for (int i = 0; i < roots.Length; i++)
            {
                posErr = Mathf.Max(posErr, (roots[i].transform.position - (R * p0[i] + T)).magnitude);
                rotErr = Mathf.Max(rotErr, Quaternion.Angle(roots[i].transform.rotation, R * q0[i]));
            }
            Check("P2: every root at R·p0 + T ± 1e-4 (rotation R·r0)", committed.Ok && posErr <= 1e-4f && rotErr <= 0.01f, $"{committed}, max position error {posErr:E2}, rotation {rotErr:E2} deg");
            if (map.StaticBatchedRenderers > 0) Line($"P2 note (D2): {map.StaticBatchedRenderers} static-batched renderers; finding map.staticBatchMoved {session.Findings.Has("map.staticBatchMoved")}");
            var undo = session.Commands.Undo();
            yield return null;
            posErr = rotErr = 0f;
            for (int i = 0; i < roots.Length; i++)
            {
                posErr = Mathf.Max(posErr, (roots[i].transform.position - p0[i]).magnitude);
                rotErr = Mathf.Max(rotErr, Quaternion.Angle(roots[i].transform.rotation, q0[i]));
            }
            Check("P2: undo → identity pose ± 1e-4", undo.Ok && posErr <= 1e-4f && rotErr <= 0.01f, $"{undo}, max position error {posErr:E2}, rotation {rotErr:E2} deg");

            // ---------------------------------------------------------------- P3 map clear
            var scene = map.Scene;
            var bundle = map.Bundle;
            var someRoot = roots[0];
            int nodesBefore = session.Model.Count;
            sw.Restart();
            res = session.SetMap(null);
            yield return PlayModeHarness.WaitUntil(() => map.State == MapState.None && !map.IsBusy && map.PendingUnloads == 0, 60, ok);
            Line($"P3 clear: {sw.ElapsedMilliseconds} ms");
            Check("P3: SetMap(null) ok, map scene unloaded", res.Ok && ok[0] && !scene.isLoaded, $"{res}, state {map.State}, pending unloads {map.PendingUnloads}");
            Check("P3: map roots destroyed", someRoot == null);
            Check("P3: the runtime's scene is active again", SceneManager.GetActiveScene() == appScene, SceneManager.GetActiveScene().name);
            Check("P3: map bundle unloaded", bundle == null);
            Check("P3: scene nodes still present and built", session.Model.Count == nodesBefore && rt.FindChara(bra)?.State == RealizationState.Built && rt.Find(itemB)?.HasHolder == true,
                  $"{session.Model.Count} nodes");

            // ---------------------------------------------------------------- P4 mini acceptance
            undo = session.Commands.Undo();   // the map comes back through undo
            sw.Restart();
            yield return PlayModeHarness.WaitUntil(() => map.State != MapState.None && !map.IsBusy, 120, ok);
            Check("P4: undo of the clear reloads the map", undo.Ok && ok[0] && map.State == MapState.Loaded, $"{undo}, {map.State} after {sw.ElapsedMilliseconds} ms (job {map.LoadMs} ms)");
            var anim = session.SetAnimation(bra, new AnimeRef(ContentIds.Vanilla, 19, 0, 0));
            yield return null;
            var motion = rt.FindChara(bra)?.Motion;
            int steps0 = motion ? motion.StepCount : 0;
            Check("P4: Idle_00 set, AutoStep on (play mode)", anim.Ok && motion && motion.AutoStep && rt.FindChara(bra).Animation?.Current?.Clip == "Idle_00",
                  $"{anim}, clip {rt.FindChara(bra)?.Animation?.Current?.Clip}");
            yield return PlayModeHarness.WaitFrames(60);
            int stepped = motion ? motion.StepCount - steps0 : 0;
            Check("P4: the character stepped with AutoStep for 60 frames", stepped >= 60, $"{stepped} steps");

            byte[] s1 = null;
            try { s1 = rt.Ops.ToBytes(SaveOpts()); }
            catch (Exception e) { Check("P4: save S1", false, e.ToString()); }
            Check("P4: save S1", s1 != null && s1.Length > 0, $"{s1?.Length ?? 0} bytes; findings {Codes(session.Findings)}");
            rt.Dispose();
            yield return PlayModeHarness.WaitUntil(() => map.PendingUnloads == 0, 60, ok);
            if (s1 == null) yield break;

            var services2 = new RuntimeServices { Catalog = services.Catalog, Db = services.Db, Hs2Root = services.Hs2Root, Settings = new SceneRuntimeSettings { ManualStepping = true } };
            var session2 = new SceneSession(services2.NewSessionOptions());
            var rt2 = new SceneRuntime(session2, services2);
            AddParts(rt2);
            var map2 = rt2.GetPart<MapRealization>();
            sw.Restart();
            var load = rt2.Ops.Load(s1, new LoadOptions());
            Check("P4: load S1 into a new session", load.Ok, load.Ok ? $"{session2.Model.Count} nodes" : load.Error?.ToString());
            if (!load.Ok) { rt2.Dispose(); yield break; }
            yield return PlayModeHarness.WaitUntil(() => map2.State != MapState.None && !map2.IsBusy, 120, ok);
            var bra2 = rt2.FindChara(bra);
            Check("P4: map and characters realized after the load", ok[0] && map2.State == MapState.Loaded && bra2?.State == RealizationState.Built && rt2.Find(itemB)?.State == RealizationState.Built,
                  $"map {map2.State} after {sw.ElapsedMilliseconds} ms; BR-Chan {bra2?.State}, item {rt2.Find(itemB)?.State}");
            Check("P4: ManualStepping: the motion did not advance while the map loaded", bra2?.Motion != null && !bra2.Motion.AutoStep && !bra2.MotionAdvanced,
                  $"AutoStep {bra2?.Motion?.AutoStep}, steps {bra2?.Motion?.StepCount}, advanced {bra2?.MotionAdvanced}");
            byte[] s2 = null;
            try { s2 = rt2.Ops.ToBytes(SaveOpts()); }
            catch (Exception e) { Check("P4: save S2", false, e.ToString()); }
            bool same = s1 != null && s2 != null && s1.SequenceEqual(s2);
            Check("P4: S2 = S1 (byte-identical)", same, same ? $"{s2.Length} bytes" : FirstDifference(s1, s2));
            Line("session 2 findings: " + Codes(session2.Findings));
            rt2.Dispose();
            yield return PlayModeHarness.WaitUntil(() => map2.PendingUnloads == 0, 60, ok);
        }

        static string Codes(SceneFindings f) => f.Count == 0 ? "none" : string.Join(", ", f.Select(x => x.Code + (x.Count > 1 ? " x" + x.Count : "")));

        static string FirstDifference(byte[] a, byte[] b)
        {
            if (a == null || b == null) return "missing bytes";
            int n = Math.Min(a.Length, b.Length), i = 0;
            while (i < n && a[i] == b[i]) i++;
            return $"S1 {a.Length} bytes, S2 {b.Length} bytes, first difference at offset {i}";
        }

        static List<string> BuiltInMaterials(IEnumerable<GameObject> roots)
        {
            var bad = new List<string>();
            foreach (var g in roots)
                foreach (var r in g.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                    {
                        if (!m || !m.shader) continue;
                        string n = m.shader.name;
                        if (!n.StartsWith("HS2Next/", StringComparison.Ordinal) && !n.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal)) bad.Add(n);
                    }
            return bad;
        }

        static void Write()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{pass} PASS, {fail} FAIL");
            sb.AppendLine("SceneRuntimeTest.play");
            sb.Append(body);
            File.WriteAllText(PlayReportPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log($"SceneRuntimeTest.play: {pass} PASS, {fail} FAIL -> {PlayReportPath}");
        }
    }
}
